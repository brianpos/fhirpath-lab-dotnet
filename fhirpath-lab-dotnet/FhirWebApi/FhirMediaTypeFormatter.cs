/*
 * Copyright (c) 2017+ brianpos, Firely and contributors
 * See the file CONTRIBUTORS for details.
 *
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/ewoutkramer/fhir-net-api/blob/master/LICENSE
 */

using Hl7.Fhir.Model;
using System;
using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;

namespace Hl7.Fhir.WebApi
{
    public abstract class FhirMediaTypeOutputFormatter : TextOutputFormatter
    {
        public FhirMediaTypeOutputFormatter() : base()
        {
            this.SupportedEncodings.Clear();
            this.SupportedEncodings.Add(Encoding.UTF8);
        }

        protected override bool CanWriteType(Type type)
        {
            // Do we need to call the base implementation?
            // base.CanWriteType(type);
            if (type == typeof(OperationOutcome))
                return true;
            if (typeof(Resource).IsAssignableFrom(type))
                return true;
            // The null case here is to support the deleted FhirObjectResult
            if (type == null)
                return true;
            return false;
        }

		public override bool CanWriteResult(OutputFormatterCanWriteContext context)
		{
			return base.CanWriteResult(context);
		}

		const string x_correlation_id = "X-Correlation-Id";
        public override void WriteResponseHeaders(OutputFormatterWriteContext context)
        {
            base.WriteResponseHeaders(context);
            if (context.Object is Hl7.Fhir.Model.Resource resource)
            {
                // output the Last-Modified header using the RFC1123 format
                // https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings?view=netframework-4.7
                if (resource.Meta != null && resource.Meta.LastUpdated.HasValue)
                    context.HttpContext.Response.Headers.Add(HeaderNames.LastModified, resource.Meta.LastUpdated.Value.UtcDateTime.ToString("r"));
                else
                    context.HttpContext.Response.Headers.Add(HeaderNames.LastModified, DateTimeOffset.UtcNow.ToString("r"));
                if (resource.Meta != null && !String.IsNullOrEmpty(resource.Meta.VersionId))
                    context.HttpContext.Response.Headers.Add(HeaderNames.ETag, $"W/\"{resource.Meta.VersionId}\"");
                if (!string.IsNullOrEmpty(resource.Id))
                    context.HttpContext.Response.Headers.Add(HeaderNames.Location, resource.ResourceIdentity(resource.ResourceBase).OriginalString);
            }

            // echo any X-Correlation-Id Headers if encountered
            if (context.HttpContext.Request.Headers.ContainsKey(x_correlation_id))
            {
                if (!context.HttpContext.Response.Headers.ContainsKey(x_correlation_id))
                    context.HttpContext.Response.Headers.Add(x_correlation_id, context.HttpContext.Request.Headers[x_correlation_id]);
#if DEBUG
                if (context.HttpContext.Request.Headers[x_correlation_id] != context.HttpContext.Response.Headers[x_correlation_id])
                    System.Diagnostics.Trace.WriteLine($"Hl7.Fhir.WebApi.FhirMediaTypeOutputFormatter: X-Correlation-Id headers didn't match request vs response");
#endif
            }
        }
    }
}