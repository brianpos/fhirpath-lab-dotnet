
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
using System.Text;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.WebApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections;
using System.Reflection;

namespace FhirPathLab_DotNetEngine
{
    public static class FirelyFhirpathEngineTester
    {
        public static ModelInspector _inspector = ModelInspector.ForType(typeof(Patient));

        public static CapabilityStatement RunCapabilityStatement(HttpRequest req)
        {
            var resultResource = new CapabilityStatement
            {
                Title = "FHIRPath Lab DotNet expression evaluator",
                Status = PublicationStatus.Active,
                Date = "2022-07-12",
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

        public static async Task<Resource> RunFhirPathTest(HttpRequest req,
            ILogger log, string firelyVersion)
        {
            log.LogInformation("FhirPath Expression dotnet Evaluation");

            Parameters operationParameters = new Parameters();
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
                using (var stream = SerializationUtil.JsonReaderFromStream(req.Body))
                {
                    operationParameters = await _jsParser.ParseAsync<Parameters>(stream);
                }
            }

            Resource resource = operationParameters.GetResource("resource");
            string resourceId = operationParameters.GetString("resource");
            bool bValidateExpression = operationParameters.GetSingleValue<FhirBoolean>("validate")?.Value ?? false;
            string terminologyServerUrl = operationParameters.GetString("terminologyserver");
            if (resource == null && !string.IsNullOrEmpty(resourceId))
            {
                // load the resource from another server
                ResourceIdentity ri = new ResourceIdentity(resourceId);
                if (!string.IsNullOrEmpty(ri.BaseUri?.OriginalString))
                {
                    try
                    {
                        var remoteServer = new FhirClient(ri.BaseUri, new FhirClientSettings() { VerifyFhirVersion = false });
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

            var resultResource = EvaluateFhirPathTesterExpression(resourceId, resource, operationParameters.GetString("context"), operationParameters.GetString("expression"), terminologyServerUrl, operationParameters.Parameter.FirstOrDefault(p => p.Name == "variables"), firelyVersion, bValidateExpression);
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");
            return resultResource;
        }

        const string exturlJsonValue = "http://fhir.forms-lab.com/StructureDefinition/json-value";
        public static Resource EvaluateFhirPathTesterExpression(string resourceId, Resource resource, string context, string expression, string terminologyServerUrl, Parameters.ParameterComponent pcVariables, string firelyVersion, bool bValidateExpression)
        {
            var visitorContext = new JsonExpressionTreeVisitor(_inspector,
                ModelInfo.SupportedResources, ModelInfo.OpenTypes);

            var validator = new JsonExpressionTreeVisitor(_inspector,
                ModelInfo.SupportedResources, ModelInfo.OpenTypes);

            Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
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
            // outcome.SetAnnotation(new AnnotationSourceResource() { ValidatingResource = result });

            ScopedNode inputNav;
            FhirEvaluationContext evalContext;
            if (resource != null)
            {
                // result.Parameter.Add(new Parameters.ParameterComponent() { Name = "input", Resource = resource });
                inputNav = new ScopedNode(resource.ToTypedElement(_inspector));
                evalContext = new FhirEvaluationContext(inputNav);
            }
            else
            {
                inputNav = null;
                evalContext = new FhirEvaluationContext();
            }

            SymbolTable symbolTable = new SymbolTable(FhirPathCompiler.DefaultSymbolTable);
            var te = new FhirPathTerminologies() { TerminologyServerUrl = terminologyServerUrl ?? "https://sqlonfhir-r4.azurewebsites.net/fhir" };
            symbolTable.AddVar("terminologies", te);
            symbolTable.Add("expand", (FhirPathTerminologies e, string can, string p) =>
            {
                var result = te.Expand(can, p);
                if (result != null)
                    return result.ToTypedElement(_inspector);
                return null;
            });
            symbolTable.Add("expand", (FhirPathTerminologies e, string can) =>
            {
                var result = te.Expand(can, "");
                if (result != null)
                    return result.ToTypedElement(_inspector);
                return null;
            });

            symbolTable.Add("lookup", (ITypedElement a, ITypedElement b, ITypedElement c) => te.Lookup(a, b, c));
            symbolTable.Add("lookup", (ITypedElement a, ITypedElement b) => te.Lookup(a, b));
            symbolTable.Add("lookup", (ITypedElement a) => te.Lookup(a));

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
                    }
                    else if (varParam.Value != null)
                        symbolTable.AddVar(varParam.Name, varParam.Value.ToTypedElement(_inspector));
                    else if (varParam.Resource != null)
                        symbolTable.AddVar(varParam.Name, varParam.Resource.ToTypedElement(_inspector));
                    else
                        symbolTable.AddVar(varParam.Name, null);
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
                        var wr = new CommonWebResolver((uri) => new FhirClient(uri));
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
                if (bValidateExpression)
                {
                    ValidateFhirPathExpressions(resource?.TypeName ?? "Patient", context, expression, visitorContext, validator, configParameters, outcome, compiler);
                }

                xps = compiler.Compile(expression);
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

                // Execute expression
                foreach (var ctExpr in contextList)
                {
                    try
                    {
                        traceList.Clear();
                        outputValues = xps(ctExpr.Value, evalContext).ToList();
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

                        if (outputValues.Any())
                        {
                            foreach (var rawItem in outputValues)
                            {
                                var item = new[] { rawItem }.ToFhirValues().FirstOrDefault();
                                var resultPart = new Parameters.ParameterComponent() { Name = item?.TypeName ?? "(null)" };
                                partContext.Part.Add(resultPart);

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

                                foreach (var val in ti.Value.ToFhirValues())
                                {
                                    var part = new Parameters.ParameterComponent() { Name = val.TypeName };
                                    traceParam.Part.Add(part);
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

        private static void ValidateFhirPathExpressions(string resourceType, string context, string expression, JsonExpressionTreeVisitor visitorContext, JsonExpressionTreeVisitor validator, Parameters.ParameterComponent configParameters, OperationOutcome outcome, FhirPathCompiler compiler)
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
            var ce = compiler.Parse(expression);
            var rv = ce.Accept(validator);
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

        static readonly FhirJsonSerializer _jsFormatter = new FhirJsonSerializer(new SerializerSettings()
        {
            Pretty = true,
            AppendNewLine = true,
        });
        static readonly FhirJsonParser _jsParser = new FhirJsonParser(new ParserSettings()
        {
            AcceptUnknownMembers = true,
            AllowUnrecognizedEnums = true,
            PermissiveParsing = true
        });
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
