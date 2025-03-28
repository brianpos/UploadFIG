using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Newtonsoft.Json;
using System.Xml;

namespace UploadFIG
{
    public abstract class Common_Processor
    {
        public ModelInspector ModelInspector { get; protected set; }
        public List<string> SupportedResources { get; protected set; }
        public Type[] OpenTypes { get; protected set; }

        public abstract Resource ParseXml(XmlReader xr);
        public abstract Resource ParseJson(JsonReader jr);
        public abstract Resource ParseXml(string xml);
        public abstract Resource ParseJson(string json);
        public abstract Task SerializeJson(Stream stream, Resource resource);
    }
}
