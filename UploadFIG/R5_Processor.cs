﻿extern alias r5;

using System.Xml;
using Newtonsoft.Json;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Model;
using r5::Hl7.Fhir.Serialization;
using System.Text.Json;

namespace UploadFIG
{
    public class R5_Processor : Common_Processor
    {
        public R5_Processor()
        {
            ModelInspector = Hl7.Fhir.Introspection.ModelInspector.ForAssembly(typeof(r5::Hl7.Fhir.Model.Patient).Assembly);
            SupportedResources = r5::Hl7.Fhir.Model.ModelInfo.SupportedResources;

            // Add in the missed types
            // https://github.com/FirelyTeam/firely-net-sdk/issues/2640
            var ot = r5::Hl7.Fhir.Model.ModelInfo.OpenTypes.ToList();
            ot.Add(typeof(Hl7.Fhir.Model.CodeableReference));
            ot.Add(typeof(r5::Hl7.Fhir.Model.RatioRange));
            ot.Add(typeof(r5::Hl7.Fhir.Model.Availability));
            ot.Add(typeof(r5::Hl7.Fhir.Model.ExtendedContactDetail));

            OpenTypes = ot.ToArray();

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
