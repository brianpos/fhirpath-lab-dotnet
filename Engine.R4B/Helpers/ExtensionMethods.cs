using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FhirPathLab_DotNetEngine
{
    public static class ExtensionMethods
    {
        public static string GetString(this Parameters me, string name)
        {
            var value = me.Parameter.Where(s => s.Name == name).FirstOrDefault();
            if (value == null)
                return null;
            if (value.Value as FhirString != null)
                return ((FhirString)value.Value).Value;
            if (value.Value as FhirUri != null)
                return ((FhirUri)value.Value).Value;
            return null;
        }

        public static Resource GetResource(this Parameters me, string name)
        {
            var value = me.Parameter.Where(s => s.Name == name).FirstOrDefault();
            if (value == null)
                return null;
            return value.Resource;
        }

        public static Uri RequestUri(this HttpRequest request)
        {
            return new Uri(request.GetDisplayUrl());
        }

        /// <summary>
        /// Retrieve all the parameters from the Request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="excludeParameters">Do not include any parameters from the provided collection</param>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<string, string>> TupledParameters(this HttpRequest request, string[] excludeParameters = null)
        {
            var list = new List<KeyValuePair<string, string>>();

            var nvp = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri().Query);

            foreach (var pair in nvp)
            {
                if (excludeParameters == null || !excludeParameters.Contains(pair.Key))
                {
                    foreach (string val in pair.Value)
                        list.Add(new KeyValuePair<string, string>(pair.Key, val));
                }
            }
            return list;
        }
    }
}
