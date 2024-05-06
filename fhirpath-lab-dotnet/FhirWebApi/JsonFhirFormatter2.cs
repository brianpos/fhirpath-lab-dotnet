/*
 * Copyright (c) 2017+ brianpos and contributors
 * See the file CONTRIBUTORS for details.
 *
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/ewoutkramer/fhir-net-api/blob/master/LICENSE
 */

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace Hl7.Fhir.WebApi
{
    public class JsonFhirOutputFormatter2 : FhirMediaTypeOutputFormatter
    {
        Introspection.ModelInspector _mi;
		public JsonFhirOutputFormatter2(Introspection.ModelInspector mi) : base()
        {
            _mi = mi;
            foreach (var mediaType in ContentType.JSON_CONTENT_HEADERS)
            {
                SupportedMediaTypes.Add(new MediaTypeHeaderValue(mediaType));
			}
		}

        public override void WriteResponseHeaders(OutputFormatterWriteContext context)
        {
            context.ContentType = FhirMediaType.GetMediaTypeHeaderValue(context.ObjectType, ResourceFormat.Json);
            // note that the base is called last, as this may overwrite the ContentType where the resource is of type Binary
            base.WriteResponseHeaders(context);
            //   headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "fhir.resource.json" };
        }

        public override async System.Threading.Tasks.Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (selectedEncoding == null)
                throw new ArgumentNullException(nameof(selectedEncoding));

            if (context.ObjectType != null)
            {
                // The content is serialized into a memory stream not direct onto the http stream
                // as the FHIR serializer does not support async writing, however the http body
                // doesn't support sync writing
                if (typeof(Resource).IsAssignableFrom(context.ObjectType) && context.Object != null)
                {
                    var jps = new FhirJsonPocoSerializerSettings();
                    Resource r = context.Object as Resource;
                    if (r.HasAnnotation<SummaryType>() && !(r is OperationOutcome && string.IsNullOrEmpty(r.Id)))
                    {
                        var st = r.Annotation<SummaryType>();
                        switch (st)
                        {
                            case SummaryType.True:
                                jps.SummaryFilter = SerializationFilter.ForSummary();
                                break;
                            case SummaryType.Text:
                                jps.SummaryFilter = SerializationFilter.ForText();
                                break;
                            case SummaryType.Data:
                                jps.SummaryFilter = SerializationFilter.ForData();
                                break;
                            case SummaryType.Count:
                                // ??? What to do with this?
                                break;
                            case SummaryType.False:
                                break;
                        }
                    }
                    JsonSerializerOptions _serializerOptions = new JsonSerializerOptions().ForFhir(_mi, serializerSettings: jps);
                    _serializerOptions.WriteIndented = true; // make it pretty
                    await System.Text.Json.JsonSerializer.SerializeAsync(context.HttpContext.Response.Body, context.Object, _serializerOptions, context.HttpContext.RequestAborted);
                }
            }
        }
    }
}
