extern alias r5;

using System.Xml;
using Newtonsoft.Json;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using r5::Hl7.Fhir.Serialization;

namespace UploadFIG
{
    internal class R5_Processor : Common_Processor
    {
        public R5_Processor()
        {
            ModelInspector = Hl7.Fhir.Introspection.ModelInspector.ForAssembly(typeof(r5::Hl7.Fhir.Model.Patient).Assembly);
            SupportedResources = r5::Hl7.Fhir.Model.ModelInfo.SupportedResources;
            OpenTypes = r5::Hl7.Fhir.Model.ModelInfo.OpenTypes;
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
