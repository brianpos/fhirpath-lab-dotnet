using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Introspection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Hl7.Fhir.Specification.Terminology;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using P = Hl7.Fhir.ElementModel.Types;

#pragma warning disable SDK0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace FhirPathLab_DotNetEngine
{
    public class FirelyFhirpathEngineTester
    {
        public FirelyFhirpathEngineTester(ModelInspector mi, List<string> SupportedResources, Type[] OpenTypes)
        {
            _inspector = mi;
            _supportedResources = SupportedResources;
            _openTypes = OpenTypes;

            var jsSettings = new SerializerSettings()
            {
                Pretty = true,
                AppendNewLine = true,
            };
            _jsFormatter = new CommonFhirJsonSerializer(_inspector, jsSettings);
        }
        protected readonly ModelInspector _inspector;
        protected readonly List<string> _supportedResources;
        protected readonly Type[] _openTypes;
        public Func<string, FhirClientSettings, HttpMessageHandler, BaseFhirClient> CreateFhirClient;
        public Func<string, OperationOutcome> _xmlParser;
        public Func<string, OperationOutcome> _jsonParser;

        CommonFhirJsonSerializer _jsFormatter;

        public static CapabilityStatement RunCapabilityStatement(HttpRequest req)
        {
            var resultResource = new CapabilityStatement
            {
                Title = "FHIRPath Lab DotNet expression evaluator",
                Status = PublicationStatus.Active,
                Date = "2023-07-03",
                Kind = CapabilityStatementKind.Instance,
                FhirVersion = FHIRVersion.N4_0_1,
                Format = new[] { "application/fhir+json" }
            };
            resultResource.Rest.Add(new CapabilityStatement.RestComponent()
            {
                Mode = CapabilityStatement.RestfulCapabilityMode.Server,
                Security = new CapabilityStatement.SecurityComponent { Cors = true }
            });
            resultResource.Rest[0].Operation.Add(new CapabilityStatement.OperationComponent()
            {
                Name = "fhirpath",
                Definition = "http://fhirpath-lab.org/OperationDefinition/fhirpath"
            });
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            return resultResource;
        }

        public async Task<Resource> RunFhirPathTest(HttpRequest req,
            ILogger log, string firelyVersion)
        {
            log.LogInformation("FhirPath Expression dotnet Evaluation");

            Parameters operationParameters = new Parameters();
            OperationOutcome parseIssues = null;
            if (req.Method != "POST")
            {
                // read the parameters from the request query string
                foreach (var item in req.TupledParameters())
                {
                    operationParameters.Add(item.Key, new FhirString(item.Value));
                }
            }
            else
            {
                // read the FHIR parameters resource from the request body
                using (var streamReader = new System.IO.StreamReader(req.Body))
                {
                    var settings = new FhirJsonPocoDeserializerSettings()
                    {
                        AnnotateResourceParseExceptions = true,
                        ValidateOnFailedParse = true,
                        // Validator = null, // Since we can handle multiple issues, let this through
                    };
                    var ds = new BaseFhirJsonPocoDeserializer(_inspector, settings);
                    try
                    {
                        var json = await streamReader.ReadToEndAsync();
                        operationParameters = ds.DeserializeResource(json) as Parameters;
                    }
                    catch (DeserializationFailedException exception)
                    {
                        parseIssues = exception.ToOperationOutcome();
                        if (exception.PartialResult is Parameters p)
                            operationParameters = p;
                        else
                            return exception.ToOperationOutcome();
                    }
                }
            }

            Resource resource = operationParameters.GetResource(_inspector, "resource", ref parseIssues);
            string resourceId = operationParameters.GetString("resource");
            bool bValidateExpression = operationParameters.GetSingleValue<FhirBoolean>("validate")?.Value ?? false;
            bool bEnableDebugTrace = operationParameters.GetSingleValue<FhirBoolean>("debug_trace")?.Value ?? false;
            string terminologyServerUrl = operationParameters.GetString("terminologyserver");
            if (resource == null && !string.IsNullOrEmpty(resourceId))
            {
                // load the resource from another server
                ResourceIdentity ri = new ResourceIdentity(resourceId);
                if (!string.IsNullOrEmpty(ri.BaseUri?.OriginalString))
                {
                    try
                    {
                        var remoteServer = new BaseFhirClient(ri.BaseUri, _inspector, new FhirClientSettings() { VerifyFhirVersion = false });
                        resource = await remoteServer.GetAsync(ri);
                    }
                    catch (FhirOperationException fex)
                    {
                        OperationOutcome outcome = new OperationOutcome();
                        outcome.Issue.Add(new OperationOutcome.IssueComponent()
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = OperationOutcome.IssueType.NotFound,
                            Details = new CodeableConcept() { Text = $"Unable to retrieve resource {resourceId}" },
                            Diagnostics = resourceId
                        });
                        return outcome;
                    }
                }
            }

            var resultResource = EvaluateFhirPathTesterExpression(resourceId, resource, operationParameters.GetString("context"), operationParameters.GetString("expression"), terminologyServerUrl, operationParameters.Parameter.FirstOrDefault(p => p.Name == "variables"), firelyVersion, bValidateExpression, bEnableDebugTrace, parseIssues);
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");
            return resultResource;
        }

        class LoggingHandler : DelegatingHandler
        {
            public LoggingHandler(HttpMessageHandler innerHandler, Func<string, OperationOutcome> xmlParser, Func<string, OperationOutcome> jsonParser, Action<OperationOutcome> errorLogger) : base(innerHandler)
            {
                _errorLogger = errorLogger;
                _xmlParser = xmlParser;
                _jsonParser = jsonParser;
            }
            public Func<string, OperationOutcome> _xmlParser;
            public Func<string, OperationOutcome> _jsonParser;
            private Action<OperationOutcome> _errorLogger;

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return base.Send(request, cancellationToken);
            }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Console.WriteLine("Request:");
                Console.WriteLine(request.ToString());
                if (request.Content != null)
                {
                    // Console.WriteLine(await request.Content.ReadAsStringAsync());
                }
                Console.WriteLine();

                var result = await base.SendAsync(request, cancellationToken);

                Console.WriteLine("Response:");
                Console.WriteLine(result.ToString());
                if (result.Content != null)
                {
                    var resultContentString = await result.Content.ReadAsStringAsync();
                    Console.WriteLine(resultContentString);
                    if (!result.IsSuccessStatusCode)
                    {
                        if (result.Content.Headers.ContentType.MediaType.Contains("xml") == true)
                        {
                            var r = _xmlParser(resultContentString);
                            if (_errorLogger != null)
                                _errorLogger(r);
                        }
                        if (result.Content.Headers.ContentType.MediaType.Contains("json") == true)
                        {
                            var r = _jsonParser(resultContentString);
                            if (_errorLogger != null)
                                _errorLogger(r);
                        }
                    }
                }
                Console.WriteLine();
                return result;
            }
        }

        public static Base ToFhirValue(ITypedElement r)
        {
            if (r is null)
                return null;

            var fhirValue = r.Annotation<IFhirValueProvider>();
            if (fhirValue != null)
            {
                return fhirValue.FhirValue;
            }

            return r.Value switch
            {
                bool b => new FhirBoolean(b),
                long l => new Integer64(l),
                int i => new Integer(i),
                decimal dec => new FhirDecimal(dec),
                string s => new FhirString(s),
                P.Date d => new Date(d.ToString()),
                P.Time t => new Time(t.ToString()),
                P.DateTime dt => new FhirDateTime(dt.ToDateTimeOffset(TimeSpan.Zero)),
                P.Quantity q => new Quantity(q.Value, q.Unit, q.System == P.QuantityUnitSystem.UCUM ? P.Quantity.UCUM : "http://hl7.org/fhirpath/CodeSystem/calendar-units"),
                var other => (Base)other
            };
        }

        class DebugTraceNode
        {
            public string NodeLocation { get; set; }
            public IEnumerable<ITypedElement> This { get; set; }
            public IEnumerable<ITypedElement> Focus { get; set; }
            public int? Index { get; set; }
            public IEnumerable<ITypedElement> Results { get; set; }
        }

        private class TestDebugTracer : IDebugTracer
        {
            public List<string> traceOutput = new List<string>();
            private List<ExceptionDispatchInfo> exceptions = new List<ExceptionDispatchInfo>();

            public void Assert()
            {
                if (exceptions.Count == 0)
                    return; // no exceptions to throw
                System.Diagnostics.Trace.WriteLine($"Tracer exceptions: {exceptions.Count}");
                foreach (var item in exceptions)
                {
                    item.Throw();
                }
            }


            public void TraceCall(
                Expression expr,
                int contextId,
                IEnumerable<ITypedElement> focus,
                IEnumerable<ITypedElement> thisValue,
                ITypedElement index,
                IEnumerable<ITypedElement> totalValue,
                IEnumerable<ITypedElement> result,
                IEnumerable<KeyValuePair<string, IEnumerable<ITypedElement>>> variables)
            {
                // DiagnosticsDebugTracer.DebugTraceCall(expr, contextId, focus, thisValue, index, totalValue, result, variables);

                var exprName = TraceExpressionNodeName(expr);
                if (exprName == null)
                    return; // this is a node that we aren't interested in tracing (Identifier and $that)
                var pi = expr.Location as FhirPathExpressionLocationInfo;
                string output = $"{pi.RawPosition},{pi.Length},{exprName}:" +
                                $" focus={focus?.Count() ?? 0} result={result?.Count() ?? 0}";
                traceOutput.Add(output);
                if (TraceNode != null)
                {
                    try
                    {
                        TraceNode(traceOutput.Count - 1, expr, contextId,
                            focus, thisValue, index, totalValue, result);
                    }
                    catch (Exception e)
                    {
                        // swallow the exception while tracing during testing, then after evaluation
                        // is complete, we can throw them.
                        exceptions.Add(ExceptionDispatchInfo.Capture(e));
                    }
                }
            }

            public delegate void TraceNodeDelegate(int n, Expression expr, int contextId,
                IEnumerable<ITypedElement> focus,
                IEnumerable<ITypedElement> thisValue,
                ITypedElement index,
                IEnumerable<ITypedElement> totalValue,
                IEnumerable<ITypedElement> result);
            public TraceNodeDelegate TraceNode { get; set; } = null;

            public string TraceExpressionNodeName(Expression expr)
            {
                switch (expr)
                {
                    case IdentifierExpression _:
                        return null; // we don't trace IdentifierExpressions, they are just names
                    case ConstantExpression ce:
                        return "constant";
                    case ChildExpression child:
                        return child.ChildName;
                    case IndexerExpression indexer:
                        return "[]";
                    case UnaryExpression ue:
                        return ue.Op;
                    case BinaryExpression be:
                        return be.Op;
                    case FunctionCallExpression fe:
                        return fe.FunctionName;
                    case NewNodeListInitExpression:
                        return "{}";
                    case AxisExpression ae:
                        {
                            if (ae.AxisName == "that" || ae.AxisName == "this" && ae.Location == null)
                                return null;
                            return "$" + ae.AxisName;
                        }
                    case VariableRefExpression ve:
                        return "%" + ve.Name;
                }
#if DEBUG
                Debugger.Break();
#endif
                throw new Exception($"Unknown expression type: {expr.GetType().Name}");
            }

            public string DebugTraceValue(ITypedElement? item)
            {
                if (item == null)
                    return null; // possible with a null focus to kick things off

                if (item.Location == "@primitivevalue@" || item.Location == "@QuantityAsPrimitiveValue@")
                    return $"{item.Value}\t({item.InstanceType})";

                return $"{item.Value}\t({item.InstanceType})\t{item.Location}";
            }
        }

        const string exturlJsonValue = "http://fhir.forms-lab.com/StructureDefinition/json-value";
        public Resource EvaluateFhirPathTesterExpression(string resourceId, Resource resource, string context, string expression, string terminologyServerUrl, Parameters.ParameterComponent pcVariables, string firelyVersion, bool bValidateExpression, bool bEnableDebugTrace, OperationOutcome parseIssues)
        {
            var visitorContext = new JsonExpressionTreeVisitor(_inspector,
                _supportedResources, _openTypes);

            var validator = new JsonExpressionTreeVisitor(_inspector,
                _supportedResources, _openTypes);

            Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
            ExtensionMethods.PrepareLocalFhirSymbolTableFunctions();

            var result = new Parameters() { Id = "fhirpath" };
            var configParameters = new Parameters.ParameterComponent() { Name = "parameters" };
            result.Parameter.Add(configParameters);
            configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "evaluator", Value = new FhirString(firelyVersion) });
            if (!string.IsNullOrEmpty(context))
                configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "context", Value = new FhirString(context) });
            configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "expression", Value = new FhirString(expression) });
            if (!string.IsNullOrEmpty(resourceId))
                configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "resource", Value = new FhirString(resourceId) });
            else if (resource != null)
                configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = resource });
            if (!string.IsNullOrEmpty(terminologyServerUrl))
                configParameters.Part.Add(new Parameters.ParameterComponent() { Name = "terminologyServerUrl", Value = new FhirString(terminologyServerUrl) });
            if (pcVariables != null)
                configParameters.Part.Add(pcVariables);

            // op outcome just in case we get really bad issues
            OperationOutcome outcome = new OperationOutcome();
            outcome.SetAnnotation(HttpStatusCode.BadRequest);
            if (parseIssues != null)
            {
                configParameters.Part.Insert(3, new Parameters.ParameterComponent() { Name = "debugOutcome", Resource = parseIssues });
                outcome.Issue.AddRange(parseIssues.Issue);
            }
            // outcome.SetAnnotation(new AnnotationSourceResource() { ValidatingResource = result });

            ScopedNode inputNav;
            FhirEvaluationContext evalContext;
            if (resource != null)
            {
                // result.Parameter.Add(new Parameters.ParameterComponent() { Name = "input", Resource = resource });
                inputNav = new ScopedNode(resource.ToTypedElement(_inspector));
                evalContext = new FhirEvaluationContext().WithResourceOverrides(inputNav);
            }
            else
            {
                inputNav = null;
                evalContext = new FhirEvaluationContext();
            }

            if (!string.IsNullOrEmpty(terminologyServerUrl))
            {
                HttpClientHandler handler = new HttpClientHandler();
                var tsClient = new BaseFhirClient(new Uri(terminologyServerUrl), new LoggingHandler(handler, _xmlParser, _jsonParser, (outcome) => { result.Parameter.Add(new Parameters.ParameterComponent() { Name = "ts-error", Resource = outcome }); }), _inspector);
                evalContext.TerminologyService = new ExternalTerminologyService(tsClient);
            }

            SymbolTable symbolTable = new SymbolTable(FhirPathCompiler.DefaultSymbolTable);
            var te = new FhirPathTerminologies(_inspector, terminologyServerUrl ?? "https://r4.ontoserver.csiro.au/fhir");
            symbolTable.AddVar("terminologies", te);
            symbolTable.Add("expand", (FhirPathTerminologies e, string can, string p) =>
            {
                var result = te.Expand(can, p);
                if (result != null)
                    return ElementNode.CreateList(result.ToTypedElement(_inspector));
                return ElementNode.EmptyList;
            });
            symbolTable.Add("expand", (FhirPathTerminologies e, string can) =>
            {
                var result = te.Expand(can, "");
                if (result != null)
                    return ElementNode.CreateList(result.ToTypedElement(_inspector));
                return ElementNode.EmptyList;
            });

            symbolTable.Add("lookup", (ITypedElement a, ITypedElement b, ITypedElement c) => te.Lookup(a, b, c));
            symbolTable.Add("lookup", (ITypedElement a, ITypedElement b) => te.Lookup(a, b));
            symbolTable.Add("lookup", (ITypedElement a) => te.Lookup(a));

            // inject the custom debug tracing
            List<DebugTraceNode> debugTraceList = new List<DebugTraceNode>();
            var tracer = new TestDebugTracer();
            tracer.TraceNode = (n, expr, contextId, focus, thisValue, index, totalValue, result) =>
            {
                var exprName = tracer.TraceExpressionNodeName(expr);
                if (exprName == null)
                    return; // this is a node that we aren't interested in tracing (Identifier and $that)
                var pi = expr.Location as FhirPathExpressionLocationInfo;

                var traceData = new DebugTraceNode()
                {
                    NodeLocation = $"{pi?.RawPosition},{pi?.Length},{exprName}",
                    This = thisValue,
                    Focus = focus,
                    Index = index?.Value as int?,
                    Results = result
                };
                debugTraceList.Add(traceData);
            };


            // Add variables from the operation parameters
            if (pcVariables?.Part != null)
            {
                foreach (var varParam in pcVariables.Part)
                {
                    var fragmentContent = varParam.GetStringExtension(exturlJsonValue);
                    if (!string.IsNullOrEmpty(fragmentContent))
                    {
                        // need to parse out this json fragment and add this as arbitrary content
                        ISourceNode fv = null;
                        if (fragmentContent.Trim().StartsWith("[") || !fragmentContent.Trim().StartsWith("{"))
                        {
                            fragmentContent = $"{{ value: {fragmentContent}}}";
                            fv = FhirJsonNode.Parse(fragmentContent, "value");
                            symbolTable.AddVariable(varParam.Name, fv.ToTypedElement().Children());
                        }
                        else
                        {
                            fv = FhirJsonNode.Parse(fragmentContent, "value");
                            // Questionnaire_PrePopulate_Observation.AddVariable(symbolTable, varParam.Name, fv.ToTypedElement().Children());
                            symbolTable.AddVar(varParam.Name, fv.ToTypedElement());
                        }
                        System.Diagnostics.Trace.WriteLine(fragmentContent);
                        // TODO: Work out what type this is correctly - it's a fragment, how?
                        validator.RegisterVariable(varParam.Name, typeof(FhirString));
                    }
                    else if (varParam.Value != null)
                    {
                        symbolTable.AddVar(varParam.Name, varParam.Value.ToTypedElement(_inspector));
                        // Maybe this should be tweaking the type based on parsing the string value with the fhirpath engine
                        validator.RegisterVariable(varParam.Name, varParam.Value.GetType());
                    }
                    else if (varParam.Resource != null)
                    {
                        symbolTable.AddVar(varParam.Name, varParam.Resource.ToTypedElement(_inspector));
                        validator.RegisterVariable(varParam.Name, varParam.Resource.GetType());
                    }
                    else
                    {
                        symbolTable.AddVariable(varParam.Name, ElementNode.EmptyList);
                        // No value, so just going to assume that it's a string randomly
                        validator.RegisterVariable(varParam.Name, typeof(Element));
                    }
                }
            }

            // Register the tracer in the eval Context
            List<KeyValuePair<string, IEnumerable<ITypedElement>>> traceList = new List<KeyValuePair<string, IEnumerable<ITypedElement>>>();
            evalContext.Tracer = (name, values) =>
            {
                traceList.Add(new KeyValuePair<string, IEnumerable<ITypedElement>>(name, values.ToList()));
            };

            Dictionary<string, ITypedElement> resolvedItems = new Dictionary<string, ITypedElement>();
            evalContext.ElementResolver = (referenceValue) =>
            {
                if (resolvedItems.ContainsKey(referenceValue)) return resolvedItems[referenceValue];
                if (referenceValue?.StartsWith("http") == true)
                {
                    try
                    {
                        var wr = new CommonWebResolver((uri) => new BaseFhirClient(uri, _inspector));
                        var t = wr.ResolveByUri(referenceValue);
                        if (t != null)
                        {
                            var tv = new ScopedNode(t.ToTypedElement(_inspector));
                            resolvedItems.Add(referenceValue, tv);
                            return tv;
                        }
                    }
                    catch (FhirOperationException fex)
                    {
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString($"Resource '{referenceValue}' unble to be resolved:\r\n{fex.Message}") });
                        return null;
                    }
                }
                if (referenceValue?.StartsWith("#") == true && resource is DomainResource dr)
                {
                    // locate the contained resource
                    var cr = dr.Contained?.FirstOrDefault(r => "#" + r.Id == referenceValue);
                    if (cr != null)
                    {
                        var tv = new ScopedNode(cr.ToTypedElement(_inspector));
                        resolvedItems.Add(referenceValue, tv);
                        return tv;
                    }
                }
                return null;
            };

            // compile the expression
            CompiledExpression xps = null;
            var compiler = new FhirPathCompiler(symbolTable);
            try
            {
                Expression parsedExpression = compiler.Parse(expression);
                if (bValidateExpression)
                {
                    ValidateFhirPathExpressions(resource?.TypeName ?? "Patient", context, parsedExpression, visitorContext, validator, configParameters, outcome, compiler);
                }
                if (bEnableDebugTrace)
                    xps = compiler.Compile(parsedExpression, true);
                else
                    xps = compiler.Compile(parsedExpression);
            }
            catch (Exception ex)
            {
                outcome.Issue.Add(new OperationOutcome.IssueComponent()
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Exception,
                    Details = new CodeableConcept() { Text = $"Invalid expression: {ex.Message}" },
                    Diagnostics = expression
                });
                return outcome;
            }

            IEnumerable<ITypedElement> outputValues = null;
            if (xps != null)
            {
                Dictionary<string, ITypedElement> contextList = new Dictionary<string, ITypedElement>();

                // before we execute the expression, if there is a property context to run from, navigate to that one fisrt
                if (!string.IsNullOrEmpty(context))
                {
                    // inputNav = NavigateToContextProperty(evalContext, inputNav, context);
                    CompiledExpression cexpr = null;
                    try
                    {
                        cexpr = compiler.Compile(context);
                        foreach (var val in cexpr(inputNav, evalContext))
                        {
                            contextList.Add(val.Location, val);
                        }
                    }
                    catch (NullReferenceException ex)
                    {
                        if (inputNav == null)
                        {
                            outcome.Issue.Add(new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Value,
                                Details = new CodeableConcept() { Text = $"Context expression requires a resource" },
                                Diagnostics = context
                            });
                        }
                        else
                        {
                            outcome.Issue.Add(new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Exception,
                                Details = new CodeableConcept() { Text = $"Invalid context expression: {ex.Message}" },
                                Diagnostics = context
                            });
                        }
                        result.SetAnnotation<HttpStatusCode>(HttpStatusCode.BadRequest);
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString("Context expression compilation error:\r\n" + ex.Message) });
                        return outcome;
                    }
                    catch (Exception ex)
                    {
                        outcome.Issue.Add(new OperationOutcome.IssueComponent()
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = OperationOutcome.IssueType.Exception,
                            Details = new CodeableConcept() { Text = $"Invalid context expression: {ex.Message}" },
                            Diagnostics = context
                        });
                        result.SetAnnotation<HttpStatusCode>(HttpStatusCode.BadRequest);
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString("Context expression compilation error:\r\n" + ex.Message) });
                        return outcome;
                    }
                }
                else
                {
                    contextList.Add("", inputNav);
                }

                // inject the tracer
                evalContext.DebugTracer = tracer;

                // Execute expression
                foreach (var ctExpr in contextList)
                {
                    try
                    {
                        traceList.Clear();
                        if (((ctExpr.Value as ScopedNode)?.Current as IFhirValueProvider)?.FhirValue != null)
                        {
                            var res = xps(ctExpr.Value, evalContext);
                            if (res.Any())
                                outputValues = res.ToList();
                            else
                                outputValues = ElementNode.EmptyList;
                        }
                        else
                        {
                            outputValues = ElementNode.EmptyList;
                            outcome.Issue.Add(new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Value,
                                Details = new CodeableConcept() { Text = $"Context Expression output requires a resource {ctExpr.Key}" },
                                // Diagnostics = ex.Message
                            });
                        }
                    }
                    catch (NullReferenceException ex)
                    {
                        if (inputNav == null)
                        {
                            outcome.Issue.Add(new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Value,
                                Details = new CodeableConcept() { Text = $"Expression requires a resource {ctExpr.Key}" },
                                Diagnostics = ex.Message
                            });
                        }
                        else
                        {
                            outcome.Issue.Add(new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Exception,
                                Details = new CodeableConcept() { Text = $"Expression evaluation error: {ex.Message}" },
                                Diagnostics = context
                            });
                        }
                        result.SetAnnotation<HttpStatusCode>(HttpStatusCode.BadRequest);
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString("Expression evaluation error:\r\n" + ex.Message) });
                        return outcome;
                    }
                    catch (Exception ex)
                    {
                        outcome.Issue.Add(new OperationOutcome.IssueComponent()
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = OperationOutcome.IssueType.Exception,
                            Details = new CodeableConcept() { Text = $"Invalid expression: {ex.Message}" },
                            Diagnostics = context
                        });
                        result.SetAnnotation<HttpStatusCode>(HttpStatusCode.BadRequest);
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString("Expression evaluation error:\r\n" + ex.Message) });
                        return outcome;
                    }
                    try
                    {
                        var partContext = new Parameters.ParameterComponent();
                        partContext.Name = "result";
                        if (!string.IsNullOrEmpty(ctExpr.Key))
                            partContext.Value = new FhirString(ctExpr.Key);
                        result.Parameter.Add(partContext);

                        // Debug Trace context
                        var partDebugContext = new Parameters.ParameterComponent();
                        partDebugContext.Name = "debug-trace";
                        if (!string.IsNullOrEmpty(ctExpr.Key))
                            partDebugContext.Value = new FhirString(ctExpr.Key);
                        if (debugTraceList.Any())
                            result.Parameter.Add(partDebugContext);

                        if (outputValues.Any())
                        {
                            foreach (var rawItem in outputValues)
                            {
                                var item = ToFhirValue(rawItem);
                                var resultPart = new Parameters.ParameterComponent() { Name = item?.TypeName ?? "(null)" };
                                partContext.Part.Add(resultPart);
                                // read the path from the rawItem using the IShortPathGenerator
                                if ((rawItem as ScopedNode)?.Current is IShortPathGenerator spg)
                                {
                                    if (spg?.ShortPath != null)
                                        resultPart.SetStringExtension("http://fhir.forms-lab.com/StructureDefinition/resource-path", spg.ShortPath);
                                }

                                if (item is DataType dt)
                                {
                                    if (dt is FhirString str && str.Value == "")
                                        resultPart.Name = "empty-string";
                                    else
                                        resultPart.Value = dt;
                                }
                                else if (item is Resource fr)
                                    resultPart.Resource = fr;
                                else if (item != null)
                                {
                                    resultPart.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(item));
                                }
                                else
                                {
                                    var sn = rawItem.Annotation<ISourceNode>();
                                    if (sn != null)
                                    {
                                        resultPart.Name = "Object";
                                        resultPart.SetStringExtension(exturlJsonValue, sn.ToJson());
                                    }
                                }
                            }
                        }
                        // Append Trace Results
                        if (traceList.Any())
                        {
                            foreach (var ti in traceList)
                            {
                                var traceParam = new Parameters.ParameterComponent() { Name = "trace", Value = new FhirString(ti.Key) };
                                partContext.Part.Add(traceParam);

                                foreach (var rawItem in ti.Value)
                                {
                                    if (rawItem == null) continue;
                                    Base val = ToFhirValue(rawItem);
                                    var part = new Parameters.ParameterComponent() { Name = val.TypeName };
                                    traceParam.Part.Add(part);
                                    // read the path from the rawItem using the IShortPathGenerator
                                    if ((rawItem as ScopedNode)?.Current is IShortPathGenerator spg)
                                    {
                                        if (spg?.ShortPath != null)
                                            part.SetStringExtension("http://fhir.forms-lab.com/StructureDefinition/resource-path", spg.ShortPath);
                                    }

                                    if (val is DataType dt)
                                    {
                                        if (val is FhirString str && str.Value == "")
                                            part.Name = "empty-string";
                                        else
                                            part.Value = dt;
                                    }
                                    else if (val is Resource fr)
                                        part.Resource = fr;
                                    else
                                    {
                                        part.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(val));
                                    }
                                }
                            }
                            traceList.Clear();
                        }

                        // Append Debug Trace Results
                        if (debugTraceList.Any())
                        {
                            foreach (var ti in debugTraceList)
                            {
                                var traceParam = new Parameters.ParameterComponent() { Name = ti.NodeLocation };
                                partDebugContext.Part.Add(traceParam);

                                foreach (var rawItem in ti.Results)
                                {
                                    if (rawItem == null) continue;
                                    Base val = ToFhirValue(rawItem);
                                    var part = new Parameters.ParameterComponent() { Name = val.TypeName };
                                    traceParam.Part.Add(part);
                                    // read the path from the rawItem using the IShortPathGenerator
                                    if ((rawItem as ScopedNode)?.Current is IShortPathGenerator spg)
                                    {
                                        if (spg?.ShortPath != null)
                                        {
                                            if (val is not PrimitiveType)
                                            {
                                                part.Name = "resource-path";
                                                part.Value = new FhirString(spg.ShortPath);
                                                continue;
                                            }
                                            part.SetStringExtension("http://fhir.forms-lab.com/StructureDefinition/resource-path", spg.ShortPath);
                                        }
                                    }

                                    if (val is DataType dt)
                                    {
                                        if (val is FhirString str && str.Value == "")
                                            part.Name = "empty-string";
                                        else
                                            part.Value = dt;
                                    }
                                    else if (val is Resource fr)
                                        part.Resource = fr;
                                    else
                                    {
                                        part.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(val));
                                    }
                                }

                                // Also put in the FOCUS handling
                                foreach (var rawItem in ti.Focus)
                                {
                                    if (rawItem == null) continue;
                                    Base val = ToFhirValue(rawItem);
                                    var part = new Parameters.ParameterComponent() { Name = "focus-" + val.TypeName };
                                    traceParam.Part.Add(part);
                                    // read the path from the rawItem using the IShortPathGenerator
                                    if ((rawItem as ScopedNode)?.Current is IShortPathGenerator spg)
                                    {
                                        if (spg?.ShortPath != null)
                                        {
                                            if (val is not PrimitiveType)
                                            {
                                                part.Name = "focus-resource-path";
                                                part.Value = new FhirString(spg.ShortPath);
                                                continue;
                                            }
                                            part.SetStringExtension("http://fhir.forms-lab.com/StructureDefinition/resource-path", spg.ShortPath);
                                        }
                                    }

                                    if (val is DataType dt)
                                    {
                                        if (val is FhirString str && str.Value == "")
                                            part.Name = "focus-empty-string";
                                        else
                                            part.Value = dt;
                                    }
                                    else if (val is Resource fr)
                                        part.Resource = fr;
                                    else
                                    {
                                        part.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(val));
                                    }
                                }

                                // Also put in the THIS handling
                                foreach (var rawItem in ti.This)
                                {
                                    if (rawItem == null) continue;
                                    Base val = ToFhirValue(rawItem);
                                    var part = new Parameters.ParameterComponent() { Name = "this-"+val.TypeName };
                                    traceParam.Part.Add(part);
                                    // read the path from the rawItem using the IShortPathGenerator
                                    if ((rawItem as ScopedNode)?.Current is IShortPathGenerator spg)
                                    {
                                        if (spg?.ShortPath != null)
                                        {
                                            if (val is not PrimitiveType)
                                            {
                                                part.Name = "this-resource-path";
                                                part.Value = new FhirString(spg.ShortPath);
                                                continue;
                                            }
                                            part.SetStringExtension("http://fhir.forms-lab.com/StructureDefinition/resource-path", spg.ShortPath);
                                        }
                                    }

                                    if (val is DataType dt)
                                    {
                                        if (val is FhirString str && str.Value == "")
                                            part.Name = "this-empty-string";
                                        else
                                            part.Value = dt;
                                    }
                                    else if (val is Resource fr)
                                        part.Resource = fr;
                                    else
                                    {
                                        part.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(val));
                                    }
                                }

                                // and the index
                                if (ti.Index.HasValue)
                                {
                                    var part = new Parameters.ParameterComponent() { Name = "index", Value = new Hl7.Fhir.Model.Integer(ti.Index.Value) };
                                    traceParam.Part.Add(part);
                                }
                            }
                            debugTraceList.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        result.SetAnnotation<HttpStatusCode>(HttpStatusCode.BadRequest);
                        result.Parameter.Add(new Parameters.ParameterComponent() { Name = "error", Value = new FhirString($"Processing results error: ({ctExpr.Key})\r\n{ex.Message}") });
                        return result;
                    }
                }
            }

            return result;
        }

        private void ValidateFhirPathExpressions(string resourceType, string context, Expression expression, JsonExpressionTreeVisitor visitorContext, JsonExpressionTreeVisitor validator, Parameters.ParameterComponent configParameters, OperationOutcome outcome, FhirPathCompiler compiler)
        {
            var typeResource = _inspector.GetTypeForFhirType(resourceType);
            validator.RegisterVariable("resource", typeResource);

            // Validate the context Expression (if it exists)
            if (!string.IsNullOrEmpty(context))
            {
                var contextExpr = compiler.Parse(context);
                visitorContext.AddInputType(typeResource);
                var rvc = contextExpr.Accept(visitorContext);
                foreach (var t in rvc.Types)
                {
                    // TODO: Update when the signature also supports adding the CM directly
                    validator.AddInputType(t.ClassMapping.NativeType);
                    validator.RegisterVariable("context", t.ClassMapping.NativeType);
                }
                // TODO: Support multiple types going into the context?
            }
            else
            {
                validator.AddInputType(_inspector.GetTypeForFhirType(resourceType));
                validator.RegisterVariable("context", typeResource);
            }

            // Validate the Expression itself
            var rv = expression.Accept(validator);
            if (validator.Outcome.Issue.Any())
                outcome.Issue.AddRange(validator.Outcome.Issue);
            configParameters.Part.Insert(1, new Parameters.ParameterComponent() { Name = "expectedReturnType", Value = new FhirString(rv.ToString()) });
            configParameters.Part.Insert(2, new Parameters.ParameterComponent() { Name = "parseDebug", Value = new FhirString(validator.ToString()) });

            JsonSerializerSettings JsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = ShouldSerializeContractResolver.Instance,
            };
            configParameters.Part.Insert(2, new Parameters.ParameterComponent()
            {
                Name = "parseDebugTree",
                Value = new FhirString(Newtonsoft.Json.JsonConvert.SerializeObject(validator.ToJson(), JsonSettings))
            });

            if (validator.Outcome.Issue.Any())
            {
                configParameters.Part.Insert(3, new Parameters.ParameterComponent() { Name = "debugOutcome", Resource = validator.Outcome });
            }
        }
    }

    internal class ShouldSerializeContractResolver : DefaultContractResolver
    {
        public static readonly ShouldSerializeContractResolver Instance = new ShouldSerializeContractResolver();

        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            Newtonsoft.Json.Serialization.JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyType != typeof(string))
            {
                if (property.PropertyType.GetInterface(nameof(IEnumerable)) != null)
                    property.ShouldSerialize =
                        instance => (instance?.GetType().GetProperty(property.PropertyName).GetValue(instance) as IEnumerable<object>)?.Count() > 0;
            }
            return property;
        }
    }
}
