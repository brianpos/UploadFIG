extern alias r4b;
using System.Collections.Specialized;
using Firely.Fhir.Packages;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using r4b.Hl7.Fhir.Rest;
using UploadFIG.PackageHelpers;

namespace UploadFIG.Test
{
    [TestClass]
    public class ExpressionValidationTester
    {
        ExpressionValidator? _validator;

        [TestInitialize]
        public void Initialize()
        {
            // Initialize any required resources or settings before each test
            var processor = new R4B_Processor();
            _validator = new ExpressionValidatorR4B(processor, true);
            var errFiles = new List<String>();
            var settings = new Settings() { };
            var packageCache = new TempPackageCache();
            var depChecker = new DependencyChecker(settings, FHIRVersion.N4_3_0, processor.ModelInspector, packageCache, processor, errFiles);
            _validator.PreValidation(new PackageDetails(), depChecker, true, errFiles);
        }

        private static ElementDefinition CreateElement(string path, string code, string targetProfile)
        {
            var ed =  new ElementDefinition()
            {
                Path = path,
                Type = new List<ElementDefinition.TypeRefComponent>
                {
                    new ElementDefinition.TypeRefComponent { Code = code }
                }
            };
            if (!string.IsNullOrEmpty(targetProfile))
            {
                
                ed.Type.First().TargetProfile = new[] { targetProfile };
            }
            return ed;
        }

        [TestMethod]
        public void CheckValidType()
        {
            ElementDefinition element = CreateElement("Encounter.subject", "Reference", "http://hl7.org/fhir/StructureDefinition/Patient");
            long validationErrors = 0;
            _validator?.CheckElementType("structuredefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(0, validationErrors, "Patient is a valid reference type");
        }

        [TestMethod]
        public void CheckInValidType()
        {
            ElementDefinition element = CreateElement("Encounter.subject", "Reference", "http://hl7.org/fhir/StructureDefinition/Turkey");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(1, validationErrors, "Turkey is an invalid reference type");
        }

        [TestMethod]
        public void CheckValidContainedResource()
        {
            ElementDefinition element = CreateElement("Encounter.contained", "HealthcareService", "http://hl7.org/fhir/StructureDefinition/HealthcareService");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(0, validationErrors, "HealthcareService is a valid contained resource type");
        }

        [TestMethod]
        public void CheckValidContainedResource2()
        {
            ElementDefinition element = CreateElement("Parameters.parameter.resource", "HealthcareService", "http://hl7.org/fhir/StructureDefinition/HealthcareService");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(0, validationErrors, "HealthcareService is a valid contained resource type");
        }

        [TestMethod]
        public void CheckInValidProfileContainedResource()
        {
            ElementDefinition element = CreateElement("Encounter.contained", "HealthcareService", "http://hl7.org/fhir/StructureDefinition/Turkey");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(1, validationErrors, "Turkey is an invalid contained resource type");
        }

        [TestMethod]
        public void CheckInValidContainedResource()
        {
            ElementDefinition element = CreateElement("Encounter.contained", "Turkey", "http://hl7.org/fhir/StructureDefinition/HealthcareService");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(1, validationErrors, "Turkey is an invalid contained resource type");
        }

        [TestMethod]
        public void CheckInValidContainedResource2()
        {
            ElementDefinition element = CreateElement("Parameters.parameter.resource", "Turkey", "http://hl7.org/fhir/StructureDefinition/HealthcareService");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(1, validationErrors, "Turkey is an invalid parameter resource type");
        }

        [TestMethod]
        public void CheckTypeOfResourceOnInvalidProperty()
        {
            ElementDefinition element = CreateElement("Encounter.subject", "HealthcareService", "http://hl7.org/fhir/StructureDefinition/HealthcareService");
            long validationErrors = 0;
            _validator?.CheckElementType("StructureDefinition.example.json", ref validationErrors, element, element.Type.First(), element.Path);
            Assert.AreEqual(1, validationErrors, "HealthcareService is an invalid element type, apart from properties named contained/resource");
        }

    }
}
