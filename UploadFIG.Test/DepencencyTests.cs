extern alias r4b;

using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Utility;
using r4b.Hl7.Fhir.Rest;
using System.Collections.Specialized;

namespace UploadFIG.Test
{
	[TestClass]
	public class DependencyTests
	{
		[TestMethod]
		public void TestCircularDeps()
		{
			var futureCanonicals = new Dictionary<string, Resource>(); 
			var skipCanonicals = new StringCollection();

			var sdEncounter = new StructureDefinition() { Url = "sd-encounter" };
			var sdPatient = new StructureDefinition() { Url = "sd-patient" };
			var sdCareTeam = new StructureDefinition() { Url = "sd-careTeam" };
			futureCanonicals.Add(sdEncounter.Url, sdEncounter);
			futureCanonicals.Add(sdPatient.Url, sdPatient);
			futureCanonicals.Add(sdCareTeam.Url, sdCareTeam);

			// Make the encounter depend on the patient
			sdEncounter.AddAnnotation(new DependsOnCanonical("sd-patient"));
			Assert.AreEqual(false, Program.HasCircularDependency(sdEncounter, sdPatient.Url, futureCanonicals, skipCanonicals));

			// And the patient depend on the encounter
			sdPatient.AddAnnotation(new DependsOnCanonical("sd-encounter"));
			Assert.AreEqual(true, Program.HasCircularDependency(sdEncounter, sdPatient.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(true, Program.HasCircularDependency(sdPatient, sdEncounter.Url, futureCanonicals, skipCanonicals));

			// and the care team on the patient
			sdCareTeam.AddAnnotation(new DependsOnCanonical("sd-patient"));
			Assert.AreEqual(true, Program.HasCircularDependency(sdEncounter, sdPatient.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(true, Program.HasCircularDependency(sdPatient, sdEncounter.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(false, Program.HasCircularDependency(sdCareTeam, sdPatient.Url, futureCanonicals, skipCanonicals));
		}

		[TestMethod]
		public void TestCircularDeps2Hop()
		{
			var futureCanonicals = new Dictionary<string, Resource>();
			var skipCanonicals = new StringCollection();

			var sdEncounter = new StructureDefinition() { Url = "sd-encounter" };
			var sdPatient = new StructureDefinition() { Url = "sd-patient" };
			var sdCareTeam = new StructureDefinition() { Url = "sd-careTeam" };
			var sdPractitioner = new StructureDefinition() { Url = "sd-practitioner" };
			var sdGroup = new StructureDefinition() { Url = "sd-group" };
			futureCanonicals.Add(sdEncounter.Url, sdEncounter);
			futureCanonicals.Add(sdPatient.Url, sdPatient);
			futureCanonicals.Add(sdCareTeam.Url, sdCareTeam);
			futureCanonicals.Add(sdPractitioner.Url, sdPractitioner);
			futureCanonicals.Add(sdGroup.Url, sdGroup);

			// creating a 2 hop dependency enc -> ct -> pat -> prac -> ct (enc isn't circular, it points to something that is)
			sdEncounter.AddAnnotation(new DependsOnCanonical("sd-careTeam"));
			sdCareTeam.AddAnnotation(new DependsOnCanonical("sd-patient"));
			sdPatient.AddAnnotation(new DependsOnCanonical("sd-practitioner"));
			sdPractitioner.AddAnnotation(new DependsOnCanonical("sd-careTeam"));

			Assert.AreEqual(false, Program.HasCircularDependency(sdEncounter, sdCareTeam.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(true, Program.HasCircularDependency(sdPatient, sdPractitioner.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(true, Program.HasCircularDependency(sdCareTeam, sdPatient.Url, futureCanonicals, skipCanonicals));
			Assert.AreEqual(true, Program.HasCircularDependency(sdPractitioner, sdCareTeam.Url, futureCanonicals, skipCanonicals));

			// and check the bundle sorting
			Bundle bun = new Bundle();
			bun.AddResourceEntry(sdEncounter, "sdEncounter");
			bun.AddResourceEntry(sdPatient, "sdPatient");
			bun.AddResourceEntry(sdCareTeam, "sdCareTeam");
			bun.AddResourceEntry(sdPractitioner, "sdPractitioner");
			bun.AddResourceEntry(sdGroup, "sdGroup");

			Program.ReOrderBundleEntries(bun, new List<CanonicalDetails>());
			Assert.AreEqual("sdGroup", bun.Entry[0].FullUrl);
			Assert.AreEqual("sdPatient", bun.Entry[1].FullUrl);
			Assert.AreEqual("sdCareTeam", bun.Entry[2].FullUrl);
			Assert.AreEqual("sdPractitioner", bun.Entry[3].FullUrl);
			Assert.AreEqual("sdEncounter", bun.Entry[4].FullUrl);
		}
	}
}