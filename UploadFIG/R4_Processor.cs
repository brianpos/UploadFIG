extern alias r4;

using System.Xml;
using Newtonsoft.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using r4::Hl7.Fhir.Serialization;

namespace UploadFIG
{
    internal class R4_Processor : Common_Processor
    {
        public R4_Processor()
        {
            ModelInspector = Hl7.Fhir.Introspection.ModelInspector.ForAssembly(typeof(r4::Hl7.Fhir.Model.Patient).Assembly);
            SupportedResources = r4::Hl7.Fhir.Model.ModelInfo.SupportedResources;
            OpenTypes = r4::Hl7.Fhir.Model.ModelInfo.OpenTypes;
        }

        // disable validation during parsing (not its job)
        FhirXmlParser _xmlParser = new FhirXmlParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
        FhirJsonParser _jsonParser = new FhirJsonParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });

        public override Resource Parse(JsonReader jr)
        {
            return _jsonParser.Parse<Resource>(jr);
        }

        public override Resource Parse(XmlReader xr)
        {
            return _xmlParser.Parse<Resource>(xr);
        }
    }
}
