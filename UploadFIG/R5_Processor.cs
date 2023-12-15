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

			// Add in the missed types
			// https://github.com/FirelyTeam/firely-net-sdk/issues/2640
			var ot = r5::Hl7.Fhir.Model.ModelInfo.OpenTypes.ToList();
            ot.Add(typeof(r5::Hl7.Fhir.Model.CodeableReference));
			ot.Add(typeof(r5::Hl7.Fhir.Model.RatioRange));
			ot.Add(typeof(r5::Hl7.Fhir.Model.Availability));
			ot.Add(typeof(r5::Hl7.Fhir.Model.ExtendedContactDetail));

			OpenTypes = ot.ToArray();
        }

        // disable validation during parsing (not its job)
        FhirXmlParser _xmlParser = new FhirXmlParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
        FhirJsonParser _jsonParser = new FhirJsonParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });

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
	}
}
