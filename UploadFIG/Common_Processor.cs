using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Newtonsoft.Json;
using System.Xml;

namespace UploadFIG
{
    abstract class Common_Processor
    {
        public ModelInspector ModelInspector { get; protected set; }
        public List<string> SupportedResources { get; protected set; }
        public Type[] OpenTypes { get; protected set; }

        public abstract Resource Parse(XmlReader xr);
        public abstract Resource Parse(JsonReader xr);
    }
}
