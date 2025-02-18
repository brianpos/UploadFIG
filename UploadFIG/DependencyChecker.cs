extern alias r4;
extern alias r4b;
extern alias r5;

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using UploadFIG.PackageHelpers;

namespace UploadFIG
{

    public class DependencyChecker
    {
		Settings _settings;
		ModelInspector _inspector;
		FHIRVersion _fhirVersion;
		TempPackageCache _packageCache;
		public DependencyChecker(Settings settings, FHIRVersion fhirVersion, ModelInspector inspector, TempPackageCache packageCache)
		{
			_settings = settings;
			_inspector = inspector;
			_fhirVersion = fhirVersion;
			_packageCache = packageCache;
		}

		public static void VerifyDependenciesOnServer(Settings settings, BaseFhirClient clientFhir, List<CanonicalDetails> requiresCanonicals)
        {
            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            foreach (var rawCanonical in requiresCanonicals.OrderBy(c => c.canonical))
            {
                var canonical = new Canonical(rawCanonical.canonical, rawCanonical.version, null);
                Bundle existing = null;
                switch (rawCanonical.resourceType)
                {
                    case "StructureDefinition":
                        existing = clientFhir.Search<StructureDefinition>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ValueSet":
                        existing = clientFhir.Search<ValueSet>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "CodeSystem":
                        existing = clientFhir.Search<CodeSystem>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        // also check that this system is not just a fragment/empty shell
                        if (existing != null && existing.Entry.Count(e => !(e.Resource is OperationOutcome)) > 0)
                        {
                            var codeSystem = existing.Entry.First(e => e.Resource is CodeSystem).Resource as CodeSystem;
                            if (codeSystem.Concept == null || codeSystem.Concept.Count == 0 || codeSystem.Content != CodeSystemContentMode.Complete)
                            {
                                // Warn that this content is not complete
                                ConsoleEx.WriteLine(ConsoleColor.Yellow, $"CodeSystem {codeSystem.Url} has content mode {codeSystem.Content} - this may not be a complete code system");
                            }
                        }
                        break;
                    case "Questionnaire":
                        existing = clientFhir.Search("Questionnaire", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "StructureMap":
                        existing = clientFhir.Search("StructureMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ConceptMap":
                        existing = clientFhir.Search("ConceptMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                }
				if (rawCanonical.resourceType == "unknown")
                {
					ConsoleEx.WriteLine(ConsoleColor.Red, $"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t(unknown resource type to search for)");
				}
				else if (existing == null || existing.Entry.Count(e => !(e.Resource is OperationOutcome)) > 0)
                {
                    var versionList = existing.Entry.Select(e => (e.Resource as IVersionableConformanceResource)?.Version).ToList();
					var color = Console.ForegroundColor;
					if (settings.PreventDuplicateCanonicalVersions && versionList.Count > 1)
						color = ConsoleColor.Yellow;
                    ConsoleEx.WriteLine(color, $"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t{string.Join(", ", versionList)}");
                }
                else
                {
                    ConsoleEx.WriteLine(ConsoleColor.Red, $"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t(missing)");
                }
            }
        }

        /// <summary>
        /// Scan the provided set of resources and return any canonicals that are referenced by the resources.
        /// </summary>
        /// <param name="resourcesToProcess"></param>
        /// <returns></returns>
        public IEnumerable<CanonicalDetails> ScanForCanonicals(IEnumerable<Resource> resourcesToProcess)
        {
            return ScanForCanonicals(new List<CanonicalDetails>(), resourcesToProcess);
        }

		public void ScanForCanonicals(PackageDetails pd)
		{
			var requiresDirectCanonicals = ScanForCanonicals(pd.resources).ToList();
			var externalCanonicals = FilterOutCanonicals(requiresDirectCanonicals, pd.resources).ToList();
			pd.RequiresCanonicals = externalCanonicals;
		}

		/// <summary>
		/// Scan the provided set of resources and return any canonicals referenced by the resources that are not already in the initialCanonicals list.
		/// </summary>
		/// <param name="initialCanonicals"></param>
		/// <param name="resourcesToProcess"></param>
		/// <returns></returns>
		public IEnumerable<CanonicalDetails> ScanForCanonicals(IEnumerable<CanonicalDetails> initialCanonicals, IEnumerable<Resource> resourcesToProcess)
        {
            // Review this against https://hl7.org/fhir/uv/crmi/2024Jan/distribution.html#dependency-tracing
            List<CanonicalDetails> requiresCanonicals = new List<CanonicalDetails>(initialCanonicals);

            // Scan for any extensions used (to read their canonical URL)
            foreach (var resource in resourcesToProcess)
            {
                ScanForExtensions(requiresCanonicals, resource, resource);
            }

            // Scan for resource specific canonicals
            foreach (var resource in resourcesToProcess.OfType<StructureDefinition>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<ValueSet>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<CodeSystem>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR4(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR5(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR4(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR5(requiresCanonicals, resource);
            }

			// StructureMaps
			//      (structure definitions and imports)
			//      (embedded ConceptMaps) `group.rule.target.where(transform='translate').parameter[1]` // the map URI.
			// Library - DataRequirements
			// PlanDefinitions
			// OperationDefinitions?

			// Now do a last minute check to see if there are any that weren't found in properties that aren't explicitly handled
			// (and thus we don't know what type of canonical resource they are referencing)
			FhirPathCompiler compiler = new FhirPathCompiler();
			var expr = compiler.Compile("descendants().ofType(canonical)");
			foreach (var resource in resourcesToProcess)
			{
				ScopedNode node = new ScopedNode(resource.ToTypedElement(_inspector));
				var values = expr(node, new FhirEvaluationContext());
				foreach (ITypedElement value in values)
				{
					if (value == null)
						continue;
					var fhirValue = value.Annotation<IFhirValueProvider>();
					if (fhirValue.FhirValue is Canonical canonical)
					{
						// And also check the scoped node parent to see if it is an extension
						if (value is ScopedNode sn && sn.Parent?.InstanceType == "Extension")
						{
							var ext = sn.Parent.ToPoco<Extension>(_inspector);
							if (ext.Url == "http://hl7.org/fhir/StructureDefinition/structuredefinition-conformance-derivedFrom")
								continue;
							// Obligation URLs http://hl7.org/fhir/StructureDefinition/obligation
							if (ext.Url == "actor")
								continue;
							if (ext.Url == "process")
								continue;
						}

						if (string.IsNullOrEmpty(canonical.Value)) // happens if there are extensions only and no value
							continue;
						if (string.IsNullOrEmpty(canonical.Uri)) // if there is no canonical URL then skip (such as a contained ref)
							continue;
						if (resource is CodeSystem cs && cs.ValueSet == canonical.Value)
							continue;
						if (requiresCanonicals.Any(c => c.canonical == canonical.Value))
							continue;
						if (!IsCoreOrExtensionOrToolsCanonical(canonical.Value))
							CheckRequiresCanonical(resource, "unknown", canonical.Value, requiresCanonicals, (newValue) => canonical.Value = newValue);
					}
				}
			}

			return requiresCanonicals.Where(r => !initialCanonicals.Any(ic => ic.canonical == r.canonical));
        }

		private void ScanForExtensions(List<CanonicalDetails> requiresCanonicals, Resource resource, Base prop)
        {
            foreach (var child in prop.Children)
            {
                if (child is Extension extension)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", extension.Url, requiresCanonicals, (value) => extension.Url = value);
                    if (extension.Value != null)
                    {
                        // This will scan for extensions on extension values (not complex extensions)
                        ScanForExtensions(requiresCanonicals, resource, extension.Value);
                    }
                }
                else
                {
                    // We don't scan extensions (which would find complex extensions
                    ScanForExtensions(requiresCanonicals, resource, child);
                }
            }
        }

        /// <summary>
        /// Return all the canonicals that are not in the excludedResource list
        /// </summary>
        /// <param name="initialCanonicals"></param>
        /// <param name="excludeResources"></param>
        /// <returns></returns>
        public IEnumerable<CanonicalDetails> FilterOutCanonicals(IEnumerable<CanonicalDetails> initialCanonicals, IEnumerable<Resource> excludeResources)
        {
            List<CanonicalDetails> filteredCanonicals = new List<CanonicalDetails>(initialCanonicals);

            // Now check for the ones that we've internally got covered :)
            foreach (var resource in excludeResources.OfType<IVersionableConformanceResource>())
            {
                var nodes = initialCanonicals.Where(rc => rc.canonical == resource.Url).ToArray(); // not checking the type as that sometimes has "unknown" in it
                if (nodes.Any())
                {
					foreach (var node in nodes)
					{
						if (string.IsNullOrEmpty(node.version) || resource.Version == node.version)
						{
							if (node.resourceType == "unknown")
								node.resourceType = (resource as Resource).TypeName;
							filteredCanonicals.Remove(node);
						}
					}
                }
            }

            return filteredCanonicals;
        }

		public IEnumerable<CanonicalDetails> FilterCanonicals(IEnumerable<CanonicalDetails> canonicals, PackageDetails pd)
		{
			var localResourcesNotLoaded = pd.Files.Where(f => canonicals.Any(c => f.url == c.canonical && c.resource == null)).ToArray();
			var result = canonicals.Where(c => !pd.Files.Any(f => f.url == c.canonical)).ToArray();
			return result;
		}

		internal bool IsCoreOrExtensionOrToolsCanonical(string canonicalUrl)
		{
			var canonical = new Canonical(canonicalUrl);
			if (Uri.IsWellFormedUriString(canonical.Uri, UriKind.Absolute) && _inspector.IsCoreModelTypeUri(new Uri(canonicalUrl)))
				return true;

			var extCanonicals = GetExtensionCanonicals(_fhirVersion);
			if (extCanonicals.ContainsKey(canonical.Uri))
				return true;

			// Filter out any from the tools package
			if (canonicalUrl.StartsWith("http://hl7.org/fhir/tools/"))
				return true;

			if (_crossVersionCanonicalRegex.IsMatch(canonicalUrl))
				return true;

			return false;
		}
		private Regex _crossVersionCanonicalRegex = new Regex(@"^http:\/\/hl7.org\/fhir\/.\..\/StructureDefinition\/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// Return all the canonicals that are not in the core spec or core extensions pack (published with the specific release of fhir)
		/// </summary>
		/// <remarks>
		/// This is different to the general fhir extensions pack that releases itself post the release of fhir.
		/// </remarks>
		/// <param name="initialCanonicals"></param>
		/// <param name="fhirversion"></param>
		/// <param name="versionAgnosticProcessor"></param>
		/// <returns></returns>
		internal IEnumerable<CanonicalDetails> FilterOutCoreSpecAndExtensionCanonicals(IEnumerable<CanonicalDetails> initialCanonicals)
        {
            List<CanonicalDetails> filteredCanonicals = new List<CanonicalDetails>(initialCanonicals);

            // Filter the types from the core resource profiles
            var coreCanonicals = initialCanonicals.Where(v => IsCoreOrExtensionOrToolsCanonical(v.canonical)).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                filteredCanonicals.Remove(coreCanonical);
            }

			return filteredCanonicals;
        }

		// Extension canonicals
		private StringDictionary _extensionCanonicals;

		private StringDictionary GetExtensionCanonicals(FHIRVersion fhirversion)
		{
			if (_extensionCanonicals == null)
			{
				_extensionCanonicals = new StringDictionary();
				CommonZipSource zipSource = null;
				if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_0.GetLiteral()))
					zipSource = r4::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4.zip"));
				else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_3.GetLiteral()))
					zipSource = r4b::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4b.zip"));
				else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N5_0.GetLiteral()))
					zipSource = r5::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r5.zip"));
				if (zipSource != null)
				{
					zipSource.Mask = "*.xml";
					var summaries = zipSource.ListSummaries().ToArray();
					foreach (ArtifactSummary item in summaries)
					{
						var canonical = item.GetConformanceCanonicalUrl();
						if (!string.IsNullOrEmpty(canonical))
						{
							if (!_extensionCanonicals.ContainsKey(canonical))
								_extensionCanonicals.Add(canonical, item.ResourceUri);
						}
					}
				}
			}
			return _extensionCanonicals;
		}

		internal static void ExcludeKnownCanonicals(List<CanonicalDetails> requiresCanonicals, FHIRVersion fhirversion, List<Resource> resourcesToProcess, Common_Processor versionAgnosticProcessor, InMemoryResolver inMemoryResolver)
        {
            List<CanonicalDetails> allRequiredCanonicals = new List<CanonicalDetails>(requiresCanonicals);

            // Now check for the ones that we've internally got covered :)
            foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>())
            {
                var node = requiresCanonicals.FirstOrDefault(rc => rc.canonical == resource.Url);
                if (node != null)
                {
                    requiresCanonicals.Remove(node);
                }
            }

            // And the types from the core resource profiles
            var coreCanonicals = requiresCanonicals.Where(v => Uri.IsWellFormedUriString(v.canonical, UriKind.Absolute) && versionAgnosticProcessor.ModelInspector.IsCoreModelTypeUri(new Uri(v.canonical))).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }

            // And check for any Core extensions (that are packaged in the standard zip package)
            CommonZipSource zipSource = null;
            if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_0.GetLiteral()))
                zipSource = r4::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4.zip"));
            else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_3.GetLiteral()))
                zipSource = r4b::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4b.zip"));
            else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N5_0.GetLiteral()))
                zipSource = r5::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r5.zip"));
            else
            {
                // version unhandled
                Console.WriteLine($"Unhandled processing of core extensions for fhir version {fhirversion}");
                return;
            }
            // ensure that the zip file is extracted correctly before using it
            zipSource.Prepare();

            // Scan for core/core extensions dependencies
            var coreSource = new CachedResolver(zipSource);
            var extensionCanonicals = requiresCanonicals.Where(v => coreSource.ResolveByCanonicalUri(v.canonical) != null).ToList();
            foreach (var coreCanonical in extensionCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }
        }

        record DependansOnCanonical
        {
            public DependansOnCanonical(string value)
            {
                CanonicalUrl = value;
            }

            public string CanonicalUrl { get; init; }
        }

        static List<string> ignoreCanonicals = new (new string[] {
			// Extensions required by THO (but THO has no dependencies)
			"http://hl7.org/fhir/StructureDefinition/codesystem-properties-mode",
			"http://hl7.org/fhir/StructureDefinition/structuredefinition-type-characteristics",

			// Extensions that just aren't required for general production server usage, and adversely impact the dependency list artificially
			"http://hl7.org/fhir/StructureDefinition/ActorDefinition",
			"http://hl7.org/fhir/StructureDefinition/structuredefinition-conformance-derivedFrom",
		});

        private void CheckRequiresCanonical(Resource resource, string canonicalType, string canonicalUrl, List<CanonicalDetails> requiresCanonicals, Action<string> patchVersionedCanonical = null)
        {
			if (!string.IsNullOrEmpty(canonicalUrl))
            {
				Canonical c = new Canonical(canonicalUrl);
				if (ignoreCanonicals.Contains(c.Uri) || ignoreCanonicals.Contains($"{c.Uri}|{c.Version}"))
					return;
				if (_settings.IgnoreCanonicals?.Any() == true && (_settings.IgnoreCanonicals.Contains(c.Uri) || _settings.IgnoreCanonicals.Contains($"{c.Uri}|{c.Version}")))
					return;

				if (canonicalUrl.StartsWith("#") && resource is DomainResource dr)
                {
                    // local reference - check that it exists in the resource
                    var localRef = dr.Contained?.Where(c => c.Id == canonicalUrl.Substring(1));
                    if (!localRef.Any())
                        ConsoleEx.WriteLine(ConsoleColor.Yellow, $"WARNING Unable to resolve contained canonical in {resource.TypeName}/{resource.Id}: {canonicalUrl}");
                    return;
                }

				if (IsCoreOrExtensionOrToolsCanonical(c.Uri))
					return;
					
				var usedBy = requiresCanonicals.Where(s => s.canonical == c.Uri 
					&& (s.resourceType == canonicalType || canonicalType == "unknown")
					&& (string.IsNullOrEmpty(c.Version) || string.IsNullOrEmpty(s.version) || c.Version == s.version)
					);
                if (!usedBy.Any())
                {
                    var cd =
                    new CanonicalDetails()
                    {
                        canonical = c.Uri,
                        version = c.Version,
                        resourceType = canonicalType,
                    };
                    cd.requiredBy.Add(resource);
                    requiresCanonicals.Add(cd);
                }
                else
                {
                    foreach (var cd in usedBy)
                    {
                        if (!cd.requiredBy.Contains(resource))
                            cd.requiredBy.Add(resource);
                    }
                }
                resource.AddAnnotation(new DependansOnCanonical(canonicalUrl));
            }
        }

        private void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals, (value) => (resource.Source as Canonical).Value = value);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals, (value) => (resource.Target as Canonical).Value = value);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals, (value) => { group.Source = value; });
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals, (value) => { group.Target = value; });

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals, (value) => { dependsOn.System = value; });
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals, (value) => { product.System = value; });
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals, (value) => { group.Unmapped.Url = value; });
                }
            }
        }

        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals, (value) => (resource.Source as Canonical).Value = value);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals, (value) => (resource.Target as Canonical).Value = value);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals, (value) => { group.Source = value; });
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals, (value) => { group.Target = value; });

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals, (value) => { dependsOn.System = value; });
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals, (value) => { product.System = value; });
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals, (value) => { group.Unmapped.Url = value; });
                }
            }
        }

        private void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.ConceptMap resource)
        {
            foreach (var prop in resource.Property)
            {
                CheckRequiresCanonical(resource, "CodeSystem", prop.System, requiresCanonicals, (value) => { prop.System = value; });
            }
            CheckRequiresCanonical(resource, "ValueSet", resource.SourceScope as Canonical ?? (resource.SourceScope as FhirUri)?.Value, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.TargetScope as Canonical ?? (resource.TargetScope as FhirUri)?.Value, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals, (value) => { group.Source = value; });
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals, (value) => { group.Target = value; });

                foreach (var element in group.Element)
                {
                    CheckRequiresCanonical(resource, "ValueSet", element.ValueSet, requiresCanonicals, (value) => { element.ValueSet = value; });

                    foreach (var target in element.Target)
                    {
                        CheckRequiresCanonical(resource, "ValueSet", target.ValueSet, requiresCanonicals, (value) => { target.ValueSet = value; });
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", dependsOn.ValueSet, requiresCanonicals, (value) => { dependsOn.ValueSet = value; });
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", product.ValueSet, requiresCanonicals, (value) => { product.ValueSet = value; });
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ValueSet", group.Unmapped.ValueSet, requiresCanonicals, (value) => { group.Unmapped.ValueSet = value; });
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.OtherMap, requiresCanonicals, (value) => { group.Unmapped.OtherMap = value; });
                }
            }
        }


        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(resource, "CodeSystem", resource.Supplements, requiresCanonicals, (value) => { resource.Supplements = value; });
            // Removing this check for the "complete valueset" reference as this is quite often not there
            // and if others need it, they would have a reference to it.
            // CheckRequiresCanonical(resource, "ValueSet", resource.ValueSet, requiresCanonicals);

            if (resource.Content != CodeSystemContentMode.Complete || resource.Concept == null || resource.Concept.Count == 0)
            {
                // Warn that this content is not complete
                ConsoleEx.WriteLine(ConsoleColor.Yellow, $"CodeSystem {resource.Url} has content mode {resource.Content} - this may not be a complete code system");
            }
        }

        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, ValueSet resource)
        {
            if (resource?.Compose?.Include != null)
            {
                foreach (var include in resource?.Compose?.Include)
                {
                    CheckRequiresCanonical(resource, "CodeSystem", include.System, requiresCanonicals, (value) => { include.System = value; });
                    foreach (var binding in include.ValueSet)
                    {
                        CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
            }
            if (resource?.Compose?.Exclude != null)
            {
                foreach (var exclude in resource?.Compose?.Exclude)
                {
                    CheckRequiresCanonical(resource, "CodeSystem", exclude.System, requiresCanonicals, (value) => { exclude.System = value; });
                    foreach (var binding in exclude.ValueSet)
                    {
                        CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
            }
        }

        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, StructureDefinition resource)
        {
            CheckRequiresCanonical(resource, "StructureDefinition", resource.BaseDefinition, requiresCanonicals, (value) => { resource.BaseDefinition = value; });
            var compliesWithProfile = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/structuredefinition-compliesWithProfile");
            CheckRequiresCanonical(resource, "StructureDefinition", compliesWithProfile?.Value, requiresCanonicals, (value) => { compliesWithProfile.Value = value; });

            if (resource?.Differential?.Element == null)
            {
                // Nothing to process
                return;
            }

			IEnumerable<ElementDefinition> elements = resource.Differential.Element;
			if (resource?.Snapshot?.Element != null)
				elements = elements.Union(resource.Snapshot.Element);

			foreach (var ed in elements)
            {
                // Type bindings
                foreach (var t in ed.Type)
                {
                    // CheckRequiresCanonical(resource, "StructureDefinition", t.Code, requiresCanonicals);
                    foreach (var binding in t.Profile)
                    {
                        CheckRequiresCanonical(resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                    foreach (var binding in t.TargetProfile)
                    {
                        CheckRequiresCanonical(resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                }

                // Terminology Bindings
                CheckRequiresCanonical(resource, "ValueSet", ed.Binding?.ValueSet, requiresCanonicals);
                if (ed.Binding?.Additional != null) // R5 prop name, r4 uses extensions
                {
                    foreach (var binding in ed.Binding?.Additional?.Select(a => a.ValueSet))
                    {
                        CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
				var additionalBindings = ed.Binding?.GetExtensions("http://hl7.org/fhir/tools/StructureDefinition/additional-binding");
				if (additionalBindings != null)
				{
					foreach (var binding in additionalBindings)
					{
						var valueSet = binding.GetExtensionValue<Canonical>("valueSet");
						CheckRequiresCanonical(resource, "ValueSet", valueSet, requiresCanonicals, (value) => valueSet.Value = value);
					}
				}

				// value Alternatives
				foreach (var alternateExtension in ed.ValueAlternatives)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", alternateExtension, requiresCanonicals);
                }
            }
        }


        private void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR4(requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicalsMetaProfiles(List<CanonicalDetails> requiresCanonicals, Resource resource)
        {
            if (resource.Meta != null)
            {
                foreach (var profile in resource.Meta?.Profile)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", profile, requiresCanonicals);
                }
            }
        }

        private void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals, (value) => { item.AnswerValueSet = value; });
                CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals, (value) => { item.Definition = value; });

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR4(requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicals(requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4b.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals, (value) => { item.AnswerValueSet = value; });
                CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals, (value) => { item.Definition = value; });

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicals(requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR5(requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r5.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals, (value) => { item.AnswerValueSet = value; });
                CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals, (value) => { item.Definition = value; });

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR5(requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForSDCExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, DomainResource resource)
        {
            // SDC extras
            var tsm = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-targetStructureMap");
            CheckRequiresCanonical(resource, "StructureMap", tsm, requiresCanonicals, (value) => { tsm = value; });
            var qUrl = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-subQuestionnaire");
            CheckRequiresCanonical(resource, "Questionnaire", qUrl, requiresCanonicals, (value) => { qUrl = value; });
        }

        private void ScanForSDCItemExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, Element item)
        {
            // Maybe think some more about if we can dynamically also scan the extensions from the definitions and "discover" more...

            // SDC extras
            var unitValusetUrl = item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-unitValueSet");
            CheckRequiresCanonical(resource, "ValueSet", unitValusetUrl, requiresCanonicals, (value) => { unitValusetUrl = value; });
            var referenceProfile = item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-referenceProfile");
            CheckRequiresCanonical(resource, "StructureDefinition", unitValusetUrl, requiresCanonicals, (value) => { unitValusetUrl = value; });
        }

		/// <summary>
		/// Detect any resources required for this package, tagging dependent packages to load their content too
		/// </summary>
		/// <param name="pd"></param>
		/// <param name="versionAgnosticProcessor"></param>
		internal void LoadDependentResources(PackageDetails pd, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			ConsoleEx.WriteLine(ConsoleColor.Gray, $"  Loading package content for {pd.packageId}|{pd.packageVersion}");
			// Determine all the canonicals that are required for this package for the loaded resources
			var existingResources = pd.resources.ToList();
			var allRequiredCanonicals = ScanForCanonicals(pd.resources).ToList();
			var unloadedRequiredLocalResources = pd.Files.Where(f => !f.detectedInvalidContent && allRequiredCanonicals.Any(cd => cd.canonical == f.url && !pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id))).ToList();
			int safetyCatch = 0;
			while (unloadedRequiredLocalResources.Any() && safetyCatch < 10) // provide a safety net in the event that not all files load
			{
				// iteratively check if there are more contained resource that haven't been loaded as more are included in the set.
				var localCanonicals = allRequiredCanonicals.Where(c => pd.Files.Any(f => f.url == c.canonical)).ToArray();
				LoadCanonicalResource(pd, localCanonicals, versionAgnosticProcessor, errFiles);
				allRequiredCanonicals = allRequiredCanonicals.Union(ScanForCanonicals(allRequiredCanonicals, pd.resources.Except(existingResources))).ToList();
				unloadedRequiredLocalResources = pd.Files.Where(f => !f.detectedInvalidContent && allRequiredCanonicals.Any(cd => cd.canonical == f.url && !pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id))).ToList();
				safetyCatch++;
				existingResources = pd.resources.ToList();
			};
			pd.RequiresCanonicals = FilterOutCanonicals(allRequiredCanonicals, pd.resources).ToList();

			// Resolve all the required canonicals
			foreach (var canonical in pd.RequiresCanonicals)
			{
				var matches = ResolveCanonical(pd, canonical, versionAgnosticProcessor, errFiles);
				var useResource = CurrentCanonicalFromPackages.Current(matches);
				if (useResource != null)
				{
					var distinctVersionSources = matches.Select(m => ResourcePackageSource.PackageSourceVersion(m)).Distinct();
					if (distinctVersionSources.Count() > 1)
					{
						Console.Write($"    Resolved {canonical.canonical}|{canonical.version} with ");
						ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
						Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
					}
					canonical.resource = useResource as Resource;
				}
			}

			if (pd.dependencies != null)
			{
				// Now scan through the child package to ensure that it includes all of it's internally
				// referenced resources and child packages loaded
				foreach (var dep in pd.dependencies)
				{
					LoadDependentResources(dep, versionAgnosticProcessor, errFiles);
				}
			}
		}

		private void LoadCanonicalResource(PackageDetails pd, IEnumerable<CanonicalDetails> canonicals, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			// Tag items that were already loaded due to another parent package
			var itemsToBeTagged = canonicals.Where(cd => cd.resource == null && pd.Files.Any(f => f.url == cd.canonical
																&& (f.version == cd.version || string.IsNullOrEmpty(cd.version))
																&& pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id)));
			foreach (var cd in itemsToBeTagged)
			{
				var possibleFiles = pd.Files.Where(f => f.url == cd.canonical && (f.version == cd.version || string.IsNullOrEmpty(cd.version)));
				var possibleResources = pd.resources.Where(r => possibleFiles.Any(f => r.TypeName == f.resourceType && r.Id == f.id));
				cd.resource = possibleResources.FirstOrDefault();
			}

			// See if there are any remaining items that need loading
			var files = pd.Files.Where(f => canonicals.Any(cd => cd.canonical == f.url 
																&& (f.version == cd.version || string.IsNullOrEmpty(cd.version))
																&& !pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id)
															)).ToList();
			if (files.Any())
			{
				var stream = _packageCache.GetPackageStream(pd.packageId, pd.packageVersion);
				if (stream == null)
				{
					// Need to have a better mechanism here - as can't continue and should just plain barf
					// Console.WriteLine($"Cannot load package {pd.packageId}|{pd.packageVersion}");
					return;
				}
				using (stream)
				{
					foreach (var f in files)
					{
						// If the content was already discovered as invalid, no need to try again.
						if (f.detectedInvalidContent)
							continue;

						if (_settings.Verbose)
							Console.WriteLine($"    Detected {f.url}|{f.version} in {pd.packageId}|{pd.packageVersion}");
						var data = PackageReader.ReadResourceContent(stream, f.filename);
						Resource resource = null;
						try
						{
							if (f.filename.EndsWith(".xml"))
							{
								resource = versionAgnosticProcessor.ParseXml(data);
								f.resource = resource;
							}
							else if (f.filename.EndsWith(".json"))
							{
								resource = versionAgnosticProcessor.ParseJson(data);
								f.resource = resource;
							}
							else
							{
								// Not a file that we can process
								// (What about fml/map files?)
								continue;
							}
							resource.SetAnnotation(new ResourcePackageSource()
							{
								Filename = f.filename,
								PackageId = pd.packageId,
								PackageVersion = pd.packageVersion
							});

							if (resource is IVersionableConformanceResource ivr)
							{
								foreach (var can in canonicals.Where(c => c.canonical == ivr.Url))
								{
									if (can.resource == null)
									{
										can.resource = resource;
									}
									else
									{
										// This is possible a duplicate!
										if (can.resource is IVersionableConformanceResource eIvr && ivr.Version != eIvr.Version)
										{
											var options = new[] {eIvr, ivr };
											var useResource = CurrentCanonicalFromPackages.Current(options);
											if (useResource != null)
											{
												var distinctVersionSources = options.Select(m => ResourcePackageSource.PackageSourceVersion(m)).Distinct();
												if (distinctVersionSources.Count() > 1)
												{
													Console.Write($"    Resolved {can.canonical}|{can.version} with ");
													ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
													Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
												}
												can.resource = useResource as Resource;
											}
										}
									}
								}
							}
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"ERROR: ({f.filename} in {pd.packageId}|{pd.packageVersion}) {ex.Message}");
							//System.Threading.Interlocked.Increment(ref failures);
							//if (!errs.Contains(ex.Message))
							//	errs.Add(ex.Message);
							errFiles.Add(f.filename);
							f.detectedInvalidContent = true;
							continue;
						}
					}
				}
			}

			// and walk the dependencies too
			if (pd.dependencies != null)
			{
				foreach (var dep in pd.dependencies)
				{
					LoadCanonicalResource(dep, canonicals, versionAgnosticProcessor, errFiles);
				}
			}
		}

		/// <summary>
		/// Return the set of unresolved canonicals for this package, and all of its dependencies
		/// </summary>
		/// <param name="pd"></param>
		/// <returns></returns>
		public IEnumerable<CanonicalDetails> UnresolvedCanonicals(PackageDetails pd)
		{
			foreach (var canonical in pd.UnresolvedCanonicals)
				yield return canonical;
			foreach (var dep in pd.dependencies)
			{
				foreach (var canonical in UnresolvedCanonicals(dep))
					yield return canonical;
			}
		}

		/// <summary>
		/// Return all the resources loaded in this package, and all of its dependencies
		/// </summary>
		/// <param name="pd"></param>
		/// <returns></returns>
		public IEnumerable<Resource> AllResources(PackageDetails pd)
		{
			// Reverse order with dependencies first so that we can process them in the correct order
			foreach (var dep in pd.dependencies)
			{
				foreach (var resource in AllResources(dep))
					yield return resource;
			}
			// Could have a better order on these to ensure that those without dependencies are returned first
			foreach (var resource in pd.resources)
				yield return resource;
		}

		/// <summary>
		/// Scan this package, and all its dependencies for any resources that match the provided canonical URL (which could be versioned)
		/// </summary>
		/// <param name="pd"></param>
		/// <param name="canonicalUrl"></param>
		/// <param name="versionAgnosticProcessor"></param>
		/// <param name="errFiles"></param>
		/// <returns></returns>
		internal IEnumerable<IVersionableConformanceResource> ResolveCanonical(PackageDetails pd, CanonicalDetails canonicalUrl, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			// Is this in any of the dependencies?
			foreach (var dep in pd.dependencies)
			{
				foreach (var resource in ResolveCanonical(dep, canonicalUrl, versionAgnosticProcessor, errFiles))
					yield return resource;
			}

			// Is there a resource that is already included?
			foreach (var resource in pd.resources.Where(r => r is IVersionableConformanceResource ivr && ivr.Url == canonicalUrl.canonical &&
											(string.IsNullOrEmpty(canonicalUrl.version) || canonicalUrl.version == ivr.Version)))
			{
				if (resource is IVersionableConformanceResource ivr)
				{
					yield return ivr;
				}
			}

			// See if the file wasn't already loaded
			var files = pd.Files.Where(f => canonicalUrl.canonical == f.url
																&& (f.version == canonicalUrl.version || string.IsNullOrEmpty(canonicalUrl.version))
																&& !pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id)
															).ToList();
			if (files.Any())
			{
				// grab it!
				var stream = _packageCache.GetPackageStream(pd.packageId, pd.packageVersion);
				if (stream == null)
				{
					// No package to retrieve from...
					yield break;
				}
				using (stream)
				{
					foreach (var f in files)
					{
						// If the content was already discovered as invalid, no need to try again.
						if (f.detectedInvalidContent)
							continue;

						if (_settings.Verbose)
							Console.WriteLine($"    Detected {f.url}|{f.version} in {pd.packageId}|{pd.packageVersion}");
						var data = PackageReader.ReadResourceContent(stream, f.filename);
						Resource resource = null;
						try
						{
							if (f.filename.EndsWith(".xml"))
							{
								resource = versionAgnosticProcessor.ParseXml(data);
								f.resource = resource;
							}
							else if (f.filename.EndsWith(".json"))
							{
								resource = versionAgnosticProcessor.ParseJson(data);
								f.resource = resource;
							}
							else
							{
								// Not a file that we can process
								// (What about fml/map files?)
								continue;
							}
							resource.SetAnnotation(new ResourcePackageSource()
							{
								Filename = f.filename,
								PackageId = pd.packageId,
								PackageVersion = pd.packageVersion
							});
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"ERROR: ({f.filename}) {ex.Message}");
							//System.Threading.Interlocked.Increment(ref failures);
							//if (!errs.Contains(ex.Message))
							//	errs.Add(ex.Message);
							errFiles.Add(f.filename);
							f.detectedInvalidContent = true;
							continue;
						}
						yield return resource as IVersionableConformanceResource;
					}
				}
			}
		}


		/// <summary>
		/// Scan through all the resources and patch the canonical URLs in all the resources
		/// </summary>
		/// <param name="pd"></param>
		internal void PatchCanonicals(PackageDetails pd)
		{
			ConsoleEx.WriteLine(ConsoleColor.Gray, $"    Patching package content from {pd.packageId}|{pd.packageVersion}");

			FhirPathCompiler compiler = new FhirPathCompiler();
			var expr = compiler.Compile("descendants().ofType(canonical)");
			foreach (var resource in pd.resources)
			{
				ScopedNode node = new ScopedNode(resource.ToTypedElement(_inspector));
				var values = expr(node, new FhirEvaluationContext());
				foreach (ITypedElement value in values)
				{
					if (value == null)
						continue;
					var fhirValue = value.Annotation<IFhirValueProvider>();
					var sn = value as ScopedNode;
					if (fhirValue.FhirValue is Canonical canonical && sn != null)
					{
						// And also check the scoped node parent to see if it is an extension
						if (sn.Parent?.InstanceType == "Extension")
						{
							var ext = sn.Parent.ToPoco<Extension>(_inspector);
							if (ext.Url == "http://hl7.org/fhir/StructureDefinition/structuredefinition-conformance-derivedFrom")
								continue;
							// Obligation URLs http://hl7.org/fhir/StructureDefinition/obligation
							if (ext.Url == "actor")
								continue;
							if (ext.Url == "process")
								continue;
						}

						if (string.IsNullOrEmpty(canonical.Value)) // happens if there are extensions only and no value
							continue;
						if (string.IsNullOrEmpty(canonical.Uri)) // if there is no canonical URL then skip (such as a contained ref)
							continue;
						if (!string.IsNullOrEmpty(canonical.Version)) // this is already a versioned canonical so we don't want to be messing with this
							continue;
						if (resource is CodeSystem cs && cs.ValueSet == canonical.Value)
							continue;
						if (IsCoreOrExtensionOrToolsCanonical(canonical.Value))
							continue;

						// resolve this Canonical URL and replace it
						// internal package dependency
						var id = pd.Files.Where(f => f.url == canonical.Value).FirstOrDefault();
						if (id != null && id.resource is IVersionableConformanceResource ivrLocal)
						{
							canonical.Value += $"|{ivrLocal.Version}";
							if (_settings.Verbose)
								Console.WriteLine($"         + Patching {resource.TypeName}/{resource.Id} {sn.LocalLocation} = {canonical.Value}");
							continue;
						}

						var cd = pd.RequiresCanonicals.FirstOrDefault(c => c.canonical == canonical.Value && (string.IsNullOrEmpty(c.version) || string.IsNullOrEmpty(canonical.Version) || c.version == canonical.Version));
						if (cd != null && cd.resource is IVersionableConformanceResource ivr)
						{
							if (string.IsNullOrEmpty(cd.version))
								cd.version = ivr.Version;
							canonical.Value += $"|{ivr.Version}";
							if (_settings.Verbose)
								Console.WriteLine($"        >  Patching {resource.TypeName}/{resource.Id} {sn.LocalLocation} = {canonical.Value}");
							continue;
						}

						Console.WriteLine($"        ?  Skipping {resource.TypeName}/{resource.Id} {sn.LocalLocation} = {canonical.Value}");
					}
				}
				if (resource is ValueSet vs)
					{
					foreach (var include in vs.Compose?.Include)
					{
						if (!string.IsNullOrEmpty(include.System) && string.IsNullOrEmpty(include.Version))
						{
							var cd = pd.RequiresCanonicals.FirstOrDefault(c => c.canonical == include.System);
						if (cd != null && cd.resource is IVersionableConformanceResource ivr)
						{
								include.Version = ivr.Version;
							if (_settings.Verbose)
									Console.WriteLine($"        >  Patching {resource.TypeName}/{resource.Id} ValueSet.compose.include.version = {ivr.Version}");
							}
						}
					}
				}
			}

			if (pd.dependencies != null)
			{
				foreach (var dep in pd.dependencies)
				{
					PatchCanonicals(dep);
				}
			}
		}
	}
}
