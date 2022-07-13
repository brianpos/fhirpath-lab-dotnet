using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Hl7.Fhir.NetCoreApi.R4;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.WebApi;
using System.Buffers;
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
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace FhirPathLab_DotNetEngine
{
    public static class FunctionFhirPathTest

    {
        [FunctionName("FHIRPathTester-CapabilityStatement")]
        public static async Task<IActionResult> RunCapabilityStatement(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metadata")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("CapabilityStatement");

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

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter(ArrayPool<char>.Shared));
            return result;
        }

        [FunctionName("FHIRPathTester")]
        public static async Task<IActionResult> RunFhirPathTest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath")] HttpRequest req,
            ILogger log)
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
                        resource = remoteServer.Get(ri);
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
                        var br = new FhirObjectResult(HttpStatusCode.BadRequest, outcome, outcome);
                        br.Formatters.Add(new JsonFhirOutputFormatter(ArrayPool<char>.Shared));
                        return br;
                    }
                }
            }

            var resultResource = EvaluateFhirPathTesterExpression(resourceId, resource, operationParameters.GetString("context"), operationParameters.GetString("expression"), terminologyServerUrl, operationParameters.Parameter.FirstOrDefault(p => p.Name == "variables"));
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter(ArrayPool<char>.Shared));
            return result;
        }

        const string exturlJsonValue = "http://fhir.forms-lab.com/StructureDefinition/json-value";
        public static Resource EvaluateFhirPathTesterExpression(string resourceId, Resource resource, string context, string expression, string terminologyServerUrl, Parameters.ParameterComponent pcVariables)
        {
            Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
            var result = new Parameters() { Id = "fhirpath" };
            var configParameters = new Parameters.ParameterComponent() { Name = "parameters" };
            result.Parameter.Add(configParameters);
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
                inputNav = new ScopedNode(TypedSerialization.ToTypedElement(resource));
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
                    return TypedSerialization.ToTypedElement(result);
                return null;
            });
            symbolTable.Add("expand", (FhirPathTerminologies e, string can) =>
            {
                var result = te.Expand(can, "");
                if (result != null)
                    return TypedSerialization.ToTypedElement(result);
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
                        symbolTable.AddVar(varParam.Name, varParam.Value.ToTypedElement());
                    else if (varParam.Resource != null)
                        symbolTable.AddVar(varParam.Name, varParam.Resource.ToTypedElement());
                    else
                        symbolTable.AddVar(varParam.Name, null);
                }
            }

            // Register the tracer in the eval Context
            List<KeyValuePair<string, IEnumerable<ITypedElement>>> traceList = new List<KeyValuePair<string, IEnumerable<ITypedElement>>>();
            evalContext.Tracer = (name, values) =>
            {
                traceList.Add(new KeyValuePair<string, IEnumerable<ITypedElement>>(name, values));
            };

            Dictionary<string, ITypedElement> resolvedItems = new Dictionary<string, ITypedElement>();
            evalContext.ElementResolver = (referenceValue) =>
            {
                if (resolvedItems.ContainsKey(referenceValue)) return resolvedItems[referenceValue];
                if (referenceValue?.StartsWith("http") == true)
                {
                    try
                    {
                        WebResolver wr = new WebResolver();
                        var t = wr.ResolveByUri(referenceValue);
                        if (t != null)
                        {
                            var tv = new ScopedNode(TypedSerialization.ToTypedElement(t));
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
                        var tv = new ScopedNode(TypedSerialization.ToTypedElement(cr));
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
                                    resultPart.Value = dt;
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
                                    if (val is DataType dt)
                                        traceParam.Part.Add(new Parameters.ParameterComponent() { Name = dt.TypeName, Value = dt });
                                    else if (val is Resource fr)
                                        traceParam.Part.Add(new Parameters.ParameterComponent() { Name = fr.TypeName, Resource = fr });
                                    else
                                    {
                                        var jsonPart = new Parameters.ParameterComponent() { Name = val.TypeName };
                                        traceParam.Part.Add(jsonPart);
                                        jsonPart.SetStringExtension(exturlJsonValue, _jsFormatter.SerializeToString(val));
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
}
