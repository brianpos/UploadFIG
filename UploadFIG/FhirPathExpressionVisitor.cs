using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;

namespace UploadFIG
{
    public class FhirPathExpressionVisitor : BaseFhirPathExpressionVisitor
    {
        public FhirPathExpressionVisitor()
            : base(ModelInspector.ForAssembly(typeof(Patient).Assembly),
                  Hl7.Fhir.Model.ModelInfo.SupportedResources,
                  Hl7.Fhir.Model.ModelInfo.OpenTypes)
        {
        }
    }
}
