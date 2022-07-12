using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath.Expressions;
using System.Collections.Generic;

namespace FhirPathLab_DotNetEngine
{
    public static class SymbolTableExtensions
    {
        public static void AddVariable(this Hl7.FhirPath.Expressions.SymbolTable table, string name, IEnumerable<ITypedElement> value)
        {
            table.Add(name, () => { return value; });
        }
    }
}
