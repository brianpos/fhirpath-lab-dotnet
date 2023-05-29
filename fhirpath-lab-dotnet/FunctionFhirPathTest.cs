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
//using r4b::Hl7.Fhir.NetCoreApi.R4;
//using r4b::Hl7.Fhir.WebApi;
using r4b::Hl7.Fhir.Model;
using Hl7.Fhir.Model;
using Hl7.Fhir.Introspection;

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

        [FunctionName("FHIRPathTester")]
        public static async Task<IActionResult> RunFhirPathTestR4(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("FhirPath Expression dotnet Evaluation");

            var resultResource = await r4b.FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunFhirPathTest(req, log, "Firely-5.2.0 (R4B)");
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

            var resultResource = await r5.FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester.RunFhirPathTest(req, log, "Firely-5.2.0 (R5)");
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
