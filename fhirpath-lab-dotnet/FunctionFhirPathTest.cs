extern alias r4b;
extern alias r5;

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using r4b::Hl7.Fhir.Model;
using Hl7.Fhir.Model;
using Hl7.Fhir.Introspection;
using System.Net.Http;
using System.Linq;

namespace FhirPathLab_DotNetEngine
{
    public static class FunctionFhirPathTestR4B
    {
        public static ModelInspector _inspectorR4B = ModelInspector.ForType(typeof(Patient));

        [FunctionName("FHIRPathTester-CapabilityStatement")]
        public static async Task<IActionResult> RunCapabilityStatement(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metadata")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("CapabilityStatement");

            var resultResource = r4b.FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunCapabilityStatement(req);
            resultResource.FhirVersion = FHIRVersion.N4_0_1;
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new r4b::Hl7.Fhir.NetCoreApi.FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new r4b::Hl7.Fhir.WebApi.JsonFhirOutputFormatter2());
            return result;
        }

        [FunctionName("HL7Example-Downloader")]
        public static async Task<IActionResult> DownloadHl7Example(
                       [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "downloader")] HttpRequest req,
                                  ILogger log)
        {
            log.LogInformation("DownloadHl7Example");

            // This will download the example from https://hl7.org/fhir/? or https://build.fhir.org/?
            // It is required to bi-pass the CORS issues that these sites do no permit other web apps to directly request them
            // and this function app is only configured to be accessible from the fhirpath-lab app.
            string downloadExampleUrl = req.Query["url"].FirstOrDefault();

            if (!downloadExampleUrl.StartsWith("https://hl7.org/fhir/")
                && !downloadExampleUrl.StartsWith("https://build.fhir.org/"))
                return new BadRequestObjectResult("Unsupported URL");

            if (!downloadExampleUrl.EndsWith(".json")
                && !downloadExampleUrl.EndsWith(".json.html"))
                return new BadRequestObjectResult("Unsupported URL");

            if (downloadExampleUrl.EndsWith(".json.html"))
                downloadExampleUrl = downloadExampleUrl.Replace(".json.html", ".json");

            HttpClient client = new HttpClient();
            var result = await client.GetAsync(downloadExampleUrl);
            string data = await result.Content.ReadAsStringAsync();

            var response = new Microsoft.AspNetCore.Mvc.ContentResult();
            response.ContentType = result.Content.Headers.ContentType.ToString();
            response.Content = data;
            return response;
        }

        [FunctionName("FHIRPathTester")]
        public static async Task<IActionResult> RunFhirPathTestR4(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("FhirPath Expression dotnet Evaluation");

            var resultResource = await r4b.FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunFhirPathTest(req, log, "Firely-5.3.0 (R4B)");
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new r4b::Hl7.Fhir.NetCoreApi.FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new r4b::Hl7.Fhir.WebApi.JsonFhirOutputFormatter2());
            return result;
        }

        [FunctionName("FHIRPathTesterR5")]
        public static async Task<IActionResult> RunFhirPathTestR5(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath-r5")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("FhirPath Expression dotnet Evaluation");

            var resultResource = await r5.FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunFhirPathTest(req, log, "Firely-5.3.0 (R5)");
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new r5::Hl7.Fhir.NetCoreApi.FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new r5::Hl7.Fhir.WebApi.JsonFhirOutputFormatter2());
            return result;
        }

        // To keep the Azure function "warm" trigger it every 15 minutes
        // https://mikhail.io/serverless/coldstarts/azure/
        [FunctionName("Warmer")]
        public static void WarmUp([TimerTrigger("0 */15 * * * *")] TimerInfo timer)
        {
            // Do nothing
        }
    }
}
