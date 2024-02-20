using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath;
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

        internal static bool _fhirSymbolTableLocalExtensionsAdded = false;
        public static void PrepareLocalFhirSymbolTableFunctions()
        {
            if (!_fhirSymbolTableLocalExtensionsAdded)
            {
                _fhirSymbolTableLocalExtensionsAdded = true;
                FhirPathCompiler.DefaultSymbolTable.AddLocalFhirExtensions();
            }
        }

        public static SymbolTable AddLocalFhirExtensions(this SymbolTable st)
        {
            // Custom function that returns the name of the property, rather than its value
            st.Add("propname", (object f) =>
            {
                if (f is IEnumerable<ITypedElement>)
                {
                    object[] bits = (f as IEnumerable<ITypedElement>).Select(i =>
                    {
                        return i.Name;
                    }).ToArray();
                    return ElementNode.CreateList(bits);
                }
                return ElementNode.CreateList("?");
            });
            st.Add("pathname", (object f) =>
            {
                if (f is IEnumerable<ITypedElement>)
                {
                    object[] bits = (f as IEnumerable<ITypedElement>).Select(i =>
                    {
                        return i.Location;
                    }).ToArray();
                    return ElementNode.CreateList(bits);
                }
                return ElementNode.CreateList("?");
            });
            st.Add("shortpathname", (object f) =>
            {
                if (f is IEnumerable<ITypedElement>)
                {
                    var bits = (f as IEnumerable<ITypedElement>).Select(i =>
                    {
                        if ((i as ScopedNode).Current is IShortPathGenerator spg)
                        {
                            return spg.ShortPath;
                        }
                        return "?";
                    });
                    return ElementNode.CreateList(bits);
                }
                return ElementNode.CreateList("?");
            });
            return st;
        }
    }
}
