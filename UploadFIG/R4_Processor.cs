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
        FhirJsonSerializer _jsonSerializer = new FhirJsonSerializer(new SerializerSettings() { Pretty = true });

		public override Resource ParseJson(JsonReader jr)
        {
            return _jsonParser.Parse<Resource>(jr);
        }

        public override Resource ParseXml(XmlReader xr)
        {
            return _xmlParser.Parse<Resource>(xr);
        }

		public override Resource ParseJson(string json)
		{
			return _jsonParser.Parse<Resource>(json);
		}

		public override Resource ParseXml(string xml)
		{
			return _xmlParser.Parse<Resource>(xml);
		}

		public override string SerializeJson(Resource resource)
        {
			return _jsonSerializer.SerializeToString(resource);
		}
	}
}
