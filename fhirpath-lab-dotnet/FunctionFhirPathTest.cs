extern alias r4b;
extern alias r5;
extern alias r6;

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
using System.Text.Json;
using System.Threading;

namespace FhirPathLab_DotNetEngine
{
    public class FunctionFhirPathTestR4B
    {
        private const string AllowedSitesSourceUrl = "https://raw.githubusercontent.com/brianpos/hl7-diff/main/public/allowed-sites.json";
        private static readonly TimeSpan AllowedSitesCacheDuration = TimeSpan.FromHours(1);
        private static readonly TimeSpan AllowedSitesFetchTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly SemaphoreSlim AllowedSitesCacheLock = new SemaphoreSlim(1, 1);
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private static DateTimeOffset _allowedSitesCacheExpiresAt = DateTimeOffset.MinValue;
        private static List<string> _cachedAllowedSitePrefixes;
        private static readonly List<string> _fallbackAllowedSitePrefixes = new List<string>
        {
            "https://hl7.org/fhir",
            "https://github.com/HL7/",
            "https://test.ahdis.ch/matchbox",
            "https://build.fhir.org/"
        };

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

        private static ModelInspector _inspectorR6 = ModelInspector.ForAssembly(typeof(r6.Hl7.Fhir.Model.Patient).Assembly);
        List<string> _supportedResourcesR6 = r6.Hl7.Fhir.Model.ModelInfo.SupportedResources;
        Type[] _openTypesR6 = r6.Hl7.Fhir.Model.ModelInfo.OpenTypes;

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

            if (!Uri.TryCreate(downloadExampleUrl, UriKind.Absolute, out var downloadUri)
                || !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return new BadRequestObjectResult("Unsupported URL");

            var allowedSitePrefixes = await GetAllowedSitePrefixesAsync();
            if (!IsAllowedDownloadUrl(downloadExampleUrl, allowedSitePrefixes))
                return new BadRequestObjectResult("Unsupported URL");

            //if (downloadExampleUrl != null && !downloadExampleUrl.EndsWith(".json")
            //    && !downloadExampleUrl.EndsWith(".json.html"))
            //    return new BadRequestObjectResult("Unsupported URL");

            if (downloadExampleUrl.EndsWith(".json.html"))
                downloadExampleUrl = downloadExampleUrl.Replace(".json.html", ".json");
            if (downloadExampleUrl.EndsWith(".xml.html"))
                downloadExampleUrl = downloadExampleUrl.Replace(".xml.html", ".xml");
            // for github specific references, need to go to the raw endpoint
            if (downloadExampleUrl.StartsWith("https://github.com/HL7/"))
                downloadExampleUrl = downloadExampleUrl.Replace("/blob/", "/refs/heads/").Replace("https://github.com", "https://raw.githubusercontent.com");

            if (!SharedHttpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("FhirPathLabDownloadAssistant", "0.1.0"));
            }

            var result = await SharedHttpClient.GetAsync(downloadExampleUrl);
            string data = await result.Content.ReadAsStringAsync();

            var response = new Microsoft.AspNetCore.Mvc.ContentResult();
            response.ContentType = result.Content.Headers.ContentType.ToString();
            response.Content = data;
            return response;
        }

        private static async Task<List<string>> GetAllowedSitePrefixesAsync()
        {
            if (_cachedAllowedSitePrefixes != null && DateTimeOffset.UtcNow < _allowedSitesCacheExpiresAt)
                return _cachedAllowedSitePrefixes;

            await AllowedSitesCacheLock.WaitAsync();
            try
            {
                if (_cachedAllowedSitePrefixes != null && DateTimeOffset.UtcNow < _allowedSitesCacheExpiresAt)
                    return _cachedAllowedSitePrefixes;

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, AllowedSitesSourceUrl);
                    using var cts = new CancellationTokenSource(AllowedSitesFetchTimeout);
                    using var response = await SharedHttpClient.SendAsync(request, cts.Token);
                    response.EnsureSuccessStatusCode();

                    var payload = await response.Content.ReadAsStringAsync(cts.Token);
                    var parsed = ParseAllowedSitePrefixes(payload);

                    if (parsed.Count > 0)
                    {
                        _cachedAllowedSitePrefixes = parsed;
                        _allowedSitesCacheExpiresAt = DateTimeOffset.UtcNow.Add(AllowedSitesCacheDuration);
                        return _cachedAllowedSitePrefixes;
                    }
                }
                catch
                {
                    // Keep existing cached values or fall back below.
                }

                if (_cachedAllowedSitePrefixes != null)
                    return _cachedAllowedSitePrefixes;

                _cachedAllowedSitePrefixes = _fallbackAllowedSitePrefixes;
                _allowedSitesCacheExpiresAt = DateTimeOffset.UtcNow.Add(AllowedSitesCacheDuration);
                return _cachedAllowedSitePrefixes;
            }
            finally
            {
                AllowedSitesCacheLock.Release();
            }
        }

        private static List<string> ParseAllowedSitePrefixes(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("allowedSites", out var allowedSites)
                && allowedSites.ValueKind == JsonValueKind.Array)
            {
                return allowedSites.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new List<string>();
        }

        private static bool IsAllowedDownloadUrl(string downloadExampleUrl, List<string> allowedSitePrefixes)
        {
            if (!Uri.TryCreate(downloadExampleUrl, UriKind.Absolute, out var uri))
                return false;

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var urlWithoutScheme = $"{uri.Host}{uri.AbsolutePath}";

            return allowedSitePrefixes.Any(prefix =>
                downloadExampleUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || urlWithoutScheme.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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

            var resultResource = await engine.RunFhirPathTest(req, _logger, "Firely-5.13.4 (R4B)");
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

            var resultResource = await engine.RunFhirPathTest(req, _logger, "Firely-5.13.4 (R5)");
            resultResource.ResourceBase = new Uri($"{req.Scheme}://{req.Host}/api");

            var result = new FhirObjectResult(HttpStatusCode.OK, resultResource);
            result.ContentTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/fhir+json"));
            result.Formatters.Add(new JsonFhirOutputFormatter2(_inspectorR5));
            return result;
        }

        [Function("FHIRPathTesterR6")]
        public async Task<IActionResult> RunFhirPathTestR6(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "$fhirpath-r6")] HttpRequest req)
        {
            _logger?.LogInformation("FhirPath Expression dotnet Evaluation");

            var engine = new FhirPathLab_DotNetEngine.FirelyFhirpathEngineTester(_inspectorR6, _supportedResourcesR6, _openTypesR6);
            engine.CreateFhirClient = (url, settings, messageHandler) => { return new r6.Hl7.Fhir.Rest.FhirClient(url, settings, messageHandler); };
            engine._xmlParser = new r5.Hl7.Fhir.Serialization.FhirXmlParser().Parse<OperationOutcome>;
            engine._jsonParser = new r5.Hl7.Fhir.Serialization.FhirJsonParser().Parse<OperationOutcome>;

            var resultResource = await engine.RunFhirPathTest(req, _logger, "Firely-5.13.4 (R6)");
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
