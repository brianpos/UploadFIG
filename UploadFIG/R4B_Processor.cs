extern alias r4b;

using System.Xml;
using Newtonsoft.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using r4b::Hl7.Fhir.Serialization;
using System.Text.Json;

namespace UploadFIG
{
    public class R4B_Processor : Common_Processor
    {
        public R4B_Processor()
        {
            ModelInspector = Hl7.Fhir.Introspection.ModelInspector.ForAssembly(typeof(r4b::Hl7.Fhir.Model.Patient).Assembly);
            SupportedResources = r4b::Hl7.Fhir.Model.ModelInfo.SupportedResources;
            OpenTypes = r4b::Hl7.Fhir.Model.ModelInfo.OpenTypes;

            // Json writer settings
            var jps = new FhirJsonPocoSerializerSettings();
            _serializerOptions = new JsonSerializerOptions().ForFhir(serializerSettings: jps);
            _serializerOptions.WriteIndented = true; // make it pretty
        }

        // disable validation during parsing (not its job)
        FhirXmlParser _xmlParser = new FhirXmlParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
        FhirJsonParser _jsonParser = new FhirJsonParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
        JsonSerializerOptions _serializerOptions;

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

        public async override Task SerializeJson(Stream stream, Resource resource)
        {
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, resource, _serializerOptions);
        }
    }
}
