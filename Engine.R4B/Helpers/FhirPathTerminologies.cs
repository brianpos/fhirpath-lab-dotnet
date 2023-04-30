using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using System.Collections.Generic;
using System;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Model;

namespace FhirPathLab_DotNetEngine
{
    /// <summary>
    /// http://hl7.org/fhir/fhirpath.html#txapi
    /// </summary>
    public class FhirPathTerminologies : ITypedElement
    {
        public string TerminologyServerUrl { get; set; }

        public string Name => "terminologes";

        public string InstanceType => "TerminologyFhirPathExecutor";

        public object Value => this;

        public string Location => "%terminologies";

        public IElementDefinitionSummary Definition => throw new NotImplementedException();

        public IEnumerable<ITypedElement> Children(string name = null)
        {
            throw new NotImplementedException();
        }

        // expand(valueSet, params) : ValueSet
        public ValueSet Expand(string vsUrl, string parameters)
        {
            var fc = new FhirClient(TerminologyServerUrl, new FhirClientSettings() { VerifyFhirVersion = false });
            var nvp = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(parameters);
            var canUrl = new CanonicalUrl(vsUrl);
            var expParams = new Parameters();
            expParams.Add("url", new FhirUri(canUrl.Url?.Value));
            if (!string.IsNullOrEmpty(canUrl.Version?.Value))
                expParams.Add("valueSetVersion", new FhirString(canUrl.Version?.Value));

            if (nvp.ContainsKey("filter"))
                expParams.Add("filter", new FhirString(nvp["filter"]));

            if (nvp.ContainsKey("date"))
                expParams.Add("date", new FhirDateTime(nvp["date"]));

            return fc.TypeOperation<ValueSet>("expand", expParams) as ValueSet;
        }

        // lookup(coded, params) : Parameters
        public Parameters Lookup(string code, string parameters)
        {
            FhirClient fc = new FhirClient(TerminologyServerUrl, new FhirClientSettings() { VerifyFhirVersion = false });
            Parameters reqParams = ExtractLookupParameters(parameters);
            reqParams.Add("code", new Code(code));
            return fc.TypeOperation<CodeSystem>("lookup", reqParams) as Parameters;
        }

        public Parameters Lookup(Coding coding, string parameters)
        {
            FhirClient fc = new FhirClient(TerminologyServerUrl, new FhirClientSettings() { VerifyFhirVersion = false });
            Parameters reqParams = ExtractLookupParameters(parameters);
            reqParams.Add("coding", coding);
            return fc.TypeOperation<CodeSystem>("lookup", reqParams) as Parameters;
        }

        private static Parameters ExtractLookupParameters(string parameters)
        {
            var nvp = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(parameters);
            var reqParams = new Parameters();
            if (nvp.ContainsKey("system"))
                reqParams.Add("system", new FhirUri(nvp["system"]));
            if (nvp.ContainsKey("version"))
                reqParams.Add("version", new FhirString(nvp["version"]));
            if (nvp.ContainsKey("date"))
                reqParams.Add("date", new FhirDateTime(nvp["date"]));
            if (nvp.ContainsKey("displayLanguage"))
                reqParams.Add("displayLanguage", new Code(nvp["displayLanguage"]));
            if (nvp.ContainsKey("property"))
            {
                foreach (var val in nvp["property"])
                    reqParams.Add("property", new Code(val));
            }

            return reqParams;
        }

        // translate(conceptMap, coded, params) : Parameters
        public ITypedElement Lookup(ITypedElement a, ITypedElement b = null, ITypedElement c = null)
        {
            Coding coding = null;
            string code = null;
            string parameters = null;
            if (a.Value == this)
            {
                if (b.Annotation<IFhirValueProvider>() != null)
                {
                    if (b.Annotation<IFhirValueProvider>().FhirValue is Coding coding2)
                    {
                        coding = coding2;
                        if (c?.Value is string str)
                            parameters = str;
                    }
                    else if (b.Annotation<IFhirValueProvider>().FhirValue is Code code2)
                    {
                        code = code2.Value;
                        if (c?.Value is string str)
                            parameters = str;
                    }
                    else if (b.Value is string str2)
                    {
                        code = str2;
                        if (c?.Value is string str)
                            parameters = str;
                    }
                }
            }
            else
            {
                if (a.Annotation<IFhirValueProvider>() != null)
                {
                    if (a.Annotation<IFhirValueProvider>().FhirValue is Coding coding2)
                    {
                        coding = coding2;
                        if (b?.Value is string str)
                            parameters = str;
                    }
                    else if (a.Annotation<IFhirValueProvider>().FhirValue is Code code2)
                    {
                        code = code2.Value;
                        if (b?.Value is string str)
                            parameters = str;
                    }
                    else if (a.Value is string str2)
                    {
                        code = str2;
                        if (b?.Value is string str)
                            parameters = str;
                    }
                }

            }
            if (coding != null)
            {
                var result = Lookup(coding, parameters);
                if (result != null)
                    return result.ToTypedElement(FirelyFhirpathEngineTester._inspector);
            }
            if (code != null)
            {
                var result = Lookup(code, parameters);
                if (result != null)
                    return result.ToTypedElement(FirelyFhirpathEngineTester._inspector);
            }
            return null;
        }
    }
}
