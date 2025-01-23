extern alias r4b;
extern alias r5;

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Hl7.Fhir.Model;
using System.Net.Http;
using System.Linq;
using Hl7.Fhir.WebApi;
using Hl7.Fhir.Introspection;
using System.Collections.Generic;
using System.IO;

namespace FhirPathLab_DotNetEngine
{
    public class FunctionFhirPathTestR4B
    {
        public FunctionFhirPathTestR4B(ILogger<FunctionFhirPathTestR4B> logger)
        {
            _logger = logger;
        }
        private readonly ILogger<FunctionFhirPathTestR4B> _logger;

        private static ModelInspector _inspectorR4B = ModelInspector.ForAssembly(typeof(r4b.Hl7.Fhir.Model.Patient).Assembly);
        List<string> _supportedResourcesR4B = r4b.Hl7.Fhir.Model.ModelInfo.SupportedResources;
        Type[] _openTypesR4B = r4b.Hl7.Fhir.Model.ModelInfo.OpenTypes;

        private static ModelInspector _inspectorR5 = ModelInspector.ForAssembly(typeof(r5.Hl7.Fhir.Model.Patient).Assembly);
        List<string> _supportedResourcesR5 = r5.Hl7.Fhir.Model.ModelInfo.SupportedResources;
        Type[] _openTypesR5 = r5.Hl7.Fhir.Model.ModelInfo.OpenTypes;

        [Function("FHIRPathTester-CapabilityStatement")]
        public async Task<IActionResult> RunCapabilityStatement(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metadata")] HttpRequest req)
        {
            _logger?.LogInformation("CapabilityStatement");

            var resultResource = FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunCapabilityStatement(req);
            resultResource.FhirVersion = FHIRVersion.N4_0_1;
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter2(_inspectorR4B));
            return result;
        }

        [Function("HL7Example-Downloader")]
        public async Task<IActionResult> DownloadHl7Example(
                       [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "downloader")] HttpRequest req)
        {
            _logger?.LogInformation("DownloadHl7Example");

            // This will download the example from https://hl7.org/fhir/? or https://build.fhir.org/?
            // It is required to bi-pass the CORS issues that these sites do no permit other web apps to directly request them
            // and this function app is only configured to be accessible from the fhirpath-lab app.
            string downloadExampleUrl = req.Query["url"].FirstOrDefault();

            if (string.IsNullOrEmpty(downloadExampleUrl))
                return new BadRequestObjectResult("Missing URL");

            if (!downloadExampleUrl.StartsWith("https://hl7.org/fhir")
                && !downloadExampleUrl.StartsWith("https://build.fhir.org/"))
                return new BadRequestObjectResult("Unsupported URL");

            if (!downloadExampleUrl.EndsWith(".json")
                && !downloadExampleUrl.EndsWith(".json.html"))
                return new BadRequestObjectResult("Unsupported URL");

            if (downloadExampleUrl.EndsWith(".json.html"))
                downloadExampleUrl = downloadExampleUrl.Replace(".json.html", ".json");

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("FhirPathLabDownloadAssistant", "0.1.0"));
            var result = await client.GetAsync(downloadExampleUrl);
            string data = await result.Content.ReadAsStringAsync();

            var response = new Microsoft.AspNetCore.Mvc.ContentResult();
            response.ContentType = result.Content.Headers.ContentType.ToString();
            response.Content = data;
            return response;
        }

        [Function("FHIRPathTester")]
        public async Task<IActionResult> RunFhirPathTestR4(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath")] HttpRequest req)
        {
            _logger?.LogInformation("FhirPath Expression dotnet Evaluation");

            var engine = new FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester(_inspectorR4B, _supportedResourcesR4B, _openTypesR4B);
            engine.CreateFhirClient = (url, settings, messageHandler) => { return new r4b.Hl7.Fhir.Rest.FhirClient(url, settings, messageHandler); };
            engine._xmlParser = new r4b.Hl7.Fhir.Serialization.FhirXmlParser().Parse<OperationOutcome>;
            engine._jsonParser = new r4b.Hl7.Fhir.Serialization.FhirJsonParser().Parse<OperationOutcome>;
            
            var resultResource = await engine.RunFhirPathTest(req, _logger, "Firely-5.10.0 (R4B)");
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter2(_inspectorR4B));
            return result;
        }

        [Function("FHIRPathTesterR5")]
        public async Task<IActionResult> RunFhirPathTestR5(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath-r5")] HttpRequest req)
        {
            _logger?.LogInformation("FhirPath Expression dotnet Evaluation");

            var engine = new FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester(_inspectorR5, _supportedResourcesR5, _openTypesR5);
            engine.CreateFhirClient = (url, settings, messageHandler) => { return new r5.Hl7.Fhir.Rest.FhirClient(url, settings, messageHandler); };
            engine._xmlParser = new r5.Hl7.Fhir.Serialization.FhirXmlParser().Parse<OperationOutcome>;
            engine._jsonParser = new r5.Hl7.Fhir.Serialization.FhirJsonParser().Parse<OperationOutcome>;
            
            var resultResource = await engine.RunFhirPathTest(req, _logger, "Firely-5.10.0 (R5)");
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter2(_inspectorR5));
            return result;
        }

        // To keep the Azure function "warm" trigger it every 15 minutes
        // https://mikhail.io/serverless/coldstarts/azure/
        [Function("Warmer")]
        public static void WarmUp([TimerTrigger("0 */15 * * * *")] TimerInfo timer)
        {
            // Do nothing
        }
    }
}
