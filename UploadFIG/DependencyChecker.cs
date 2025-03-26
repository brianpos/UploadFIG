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
using System.Formats.Tar;
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
        Common_Processor _versionAgnosticProcessor;
        List<String> _errFiles;

        /// <summary>
        /// Cache of loaded resource instances, indexed by packageId|packageVersion|filename
        /// Used to prevent re-reading the same instance multiple times.
        /// </summary>
        Dictionary<string, Resource> _cacheResources = new Dictionary<string, Resource>();

		public DependencyChecker(Settings settings, FHIRVersion fhirVersion, ModelInspector inspector, TempPackageCache packageCache, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			_settings = settings;
			_inspector = inspector;
			_fhirVersion = fhirVersion;
			_packageCache = packageCache;
            _versionAgnosticProcessor = versionAgnosticProcessor;
            _errFiles = errFiles;
        }

		public static async Task VerifyDependenciesOnServer(Settings settings, BaseFhirClient clientFhir, List<CanonicalDetails> requiresCanonicals)
        {
            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            foreach (var rawCanonical in requiresCanonicals.OrderBy(c => c.Canonical))
            {
                var canonical = new Canonical(rawCanonical.Canonical, rawCanonical.Version, null);
                Bundle existing = null;
                switch (rawCanonical.ResourceType)
                {
                    case "StructureDefinition":
                        existing = await clientFhir.SearchAsync<StructureDefinition>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ValueSet":
                        existing = await clientFhir.SearchAsync<ValueSet>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "CodeSystem":
                        existing = await clientFhir.SearchAsync<CodeSystem>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        // also check that this system is not just a fragment/empty shell
                        if (existing != null && existing.Entry.Any(e => !(e.Resource is OperationOutcome)))
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
                        existing = await clientFhir.SearchAsync("Questionnaire", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "StructureMap":
                        existing = await clientFhir.SearchAsync("StructureMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ConceptMap":
                        existing = await clientFhir.SearchAsync("ConceptMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                }
				if (rawCanonical.ResourceType == "unknown")
				{
					ConsoleEx.WriteLine(ConsoleColor.Red, $"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t(unknown resource type to search for)");
				}
				else if (existing == null || existing.Entry.Any(e => !(e.Resource is OperationOutcome)))
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

		#region << Scan For Canonicals >>
		/// <summary>
		/// Scan the provided set of resources and return any canonicals that are referenced by the resources.
		/// </summary>
		/// <param name="resourcesToProcess"></param>
		/// <returns></returns>
		public IEnumerable<CanonicalDetails> ScanForCanonicals(PackageDetails pd, IEnumerable<FileDetail> resourcesToProcess)
        {
            // Tag them all as having been scanned
            foreach (var resource in resourcesToProcess)
            {
                resource.ScannedForDependencies = true;
            }
            return ScanForCanonicals(pd, new List<CanonicalDetails>(), resourcesToProcess.Select(f => f.resource));
        }

		/// <summary>
		/// Scan the provided set of resources and return any canonicals referenced by the resources that are not already in the initialCanonicals list.
		/// </summary>
		/// <param name="initialCanonicals"></param>
		/// <param name="resourcesToProcess"></param>
		/// <returns></returns>
		public IEnumerable<CanonicalDetails> ScanForCanonicals(PackageDetails pd, IEnumerable<CanonicalDetails> initialCanonicals, IEnumerable<Resource> resourcesToProcess)
        {
            // Review this against https://hl7.org/fhir/uv/crmi/2024Jan/distribution.html#dependency-tracing
            List<CanonicalDetails> requiresCanonicals = new List<CanonicalDetails>(initialCanonicals);

            // Scan for any extensions used (to read their canonical URL)
            foreach (var resource in resourcesToProcess)
            {
                ScanForExtensions(pd, requiresCanonicals, resource, resource);
            }

            // Scan for resource specific canonicals
            foreach (var resource in resourcesToProcess.OfType<StructureDefinition>())
            {
                ScanForCanonicals(pd, requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<ValueSet>())
            {
                ScanForCanonicals(pd, requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<CodeSystem>())
            {
                ScanForCanonicals(pd, requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR4(pd, requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicals(pd, requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR5(pd, requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR4(pd, requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicals(pd, requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR5(pd, requiresCanonicals, resource);
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
						if (requiresCanonicals.Any(c => c.Canonical == canonical.Value))
							continue;
						if (!IsCoreOrExtensionOrToolsCanonical(canonical.Value))
							CheckRequiresCanonical(pd, resource, "unknown", canonical.Value, requiresCanonicals);
					}
				}
			}

			return requiresCanonicals.Where(r => !initialCanonicals.Any(ic => ic.Canonical == r.Canonical));
        }

		private void ScanForExtensions(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource, Base prop)
        {
            foreach (var child in prop.Children)
            {
                if (child is Extension extension)
                {
                    CheckRequiresCanonical(pd, resource, "StructureDefinition", extension.Url, requiresCanonicals);
                    if (extension.Value != null)
                    {
                        // This will scan for extensions on extension values (not complex extensions)
                        ScanForExtensions(pd, requiresCanonicals, resource, extension.Value);
                    }
                }
                else
                {
                    // We don't scan extensions (which would find complex extensions
                    ScanForExtensions(pd, requiresCanonicals, resource, child);
                }
            }
        }

    	public IEnumerable<CanonicalDetails> ExcludeLocalCanonicals(PackageDetails pd, IEnumerable<CanonicalDetails> canonicals)
		{
			var result = canonicals.Where(c => !pd.Files.Any(f => f.url == c.Canonical)).ToArray();
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

        static List<string> ignoreCanonicals = new (new string[] {
			// Extensions required by THO (but THO has no dependencies)
			"http://hl7.org/fhir/StructureDefinition/codesystem-properties-mode",
			"http://hl7.org/fhir/StructureDefinition/structuredefinition-type-characteristics",

			// Extensions that just aren't required for general production server usage, and adversely impact the dependency list artificially
			"http://hl7.org/fhir/StructureDefinition/ActorDefinition",
			"http://hl7.org/fhir/StructureDefinition/structuredefinition-conformance-derivedFrom",

			// Known VSAC invalid canonical that's in lots of their older packages
			"vsacOpModifier",
		});

        private void CheckRequiresCanonical(PackageDetails pd, Resource resource, string canonicalType, string canonicalUrl, List<CanonicalDetails> requiresCanonicals)
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
					
				var usedBy = requiresCanonicals.Where(s => s.Canonical == c.Uri 
					&& (s.ResourceType == canonicalType || canonicalType == "unknown")
					&& (string.IsNullOrEmpty(c.Version) || string.IsNullOrEmpty(s.Version) || c.Version == s.Version)
					);
                if (!usedBy.Any())
                {
                    var cd =
                    new CanonicalDetails()
                    {
                        Canonical = c.Uri,
                        Version = c.Version,
                        ResourceType = canonicalType,
                    };
                    cd.requiredBy.Add(resource);
                    requiresCanonicals.Add(cd);

                    var matches = ResolveCanonical(pd, cd, _versionAgnosticProcessor, _errFiles);
                    var useResource = CurrentCanonicalFromPackages.Current(matches);
                    if (useResource != null)
                    {
                        var distinctVersionSources = matches.Select(m => ResourcePackageSource.PackageSourceVersion(m)).Distinct();
                        if (distinctVersionSources.Count() > 1 && _settings.Verbose)
                        {
                            Console.Write($"    Resolved {cd.Canonical}|{cd.Version} with ");
                            ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
                            Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
                        }
                        useResource.MarkUsedBy(cd);
                        cd.resource = useResource.resource as Resource;
                        if (!useResource.ScannedForDependencies.HasValue)
                            useResource.ScannedForDependencies = false;
                    }
                }
                else
                {
                    foreach (var cd in usedBy)
                    {
                        if (!cd.requiredBy.Contains(resource))
                            cd.requiredBy.Add(resource);
                    }
                }
				var deps = resource.Annotations<DependsOnCanonical>();
				if (!deps.Any(d => d.CanonicalUrl == canonicalUrl))
					resource.AddAnnotation(new DependsOnCanonical(canonicalUrl));
            }
        }

        private void ScanForCanonicalsR4(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(pd, resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(pd, resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(pd, resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(pd, resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(pd, resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(pd, resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private void ScanForCanonicalsR5(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.ConceptMap resource)
        {
            foreach (var prop in resource.Property)
            {
                CheckRequiresCanonical(pd, resource, "CodeSystem", prop.System, requiresCanonicals);
            }
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.SourceScope as Canonical ?? (resource.SourceScope as FhirUri)?.Value, requiresCanonicals);
            CheckRequiresCanonical(pd, resource, "ValueSet", resource.TargetScope as Canonical ?? (resource.TargetScope as FhirUri)?.Value, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    CheckRequiresCanonical(pd, resource, "ValueSet", element.ValueSet, requiresCanonicals);

                    foreach (var target in element.Target)
                    {
                        CheckRequiresCanonical(pd, resource, "ValueSet", target.ValueSet, requiresCanonicals);
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(pd, resource, "ValueSet", dependsOn.ValueSet, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(pd, resource, "ValueSet", product.ValueSet, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(pd, resource, "ValueSet", group.Unmapped.ValueSet, requiresCanonicals);
                    CheckRequiresCanonical(pd, resource, "ConceptMap", group.Unmapped.OtherMap, requiresCanonicals);
                }
            }
        }


        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(pd, resource, "CodeSystem", resource.Supplements, requiresCanonicals);
            // Removing this check for the "complete valueset" reference as this is quite often not there
            // and if others need it, they would have a reference to it.
            // CheckRequiresCanonical(resource, "ValueSet", resource.ValueSet, requiresCanonicals);

            if (resource.Content != CodeSystemContentMode.Complete || resource.Concept == null || resource.Concept.Count == 0)
            {
                // Warn that this content is not complete
                ConsoleEx.WriteLine(ConsoleColor.Yellow, $"CodeSystem {resource.Url} has content mode {resource.Content} - this may not be a complete code system");
            }
        }

        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, ValueSet resource)
        {
            if (resource?.Compose?.Include != null)
            {
                foreach (var include in resource?.Compose?.Include)
                {
                    CheckRequiresCanonical(pd, resource, "CodeSystem", include.System, requiresCanonicals);
                    foreach (var binding in include.ValueSet)
                    {
                        CheckRequiresCanonical(pd, resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
            }
            if (resource?.Compose?.Exclude != null)
            {
                foreach (var exclude in resource?.Compose?.Exclude)
                {
                    CheckRequiresCanonical(pd, resource, "CodeSystem", exclude.System, requiresCanonicals);
                    foreach (var binding in exclude.ValueSet)
                    {
                        CheckRequiresCanonical(pd, resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
            }
        }

        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, StructureDefinition resource)
        {
            CheckRequiresCanonical(pd, resource, "StructureDefinition", resource.BaseDefinition, requiresCanonicals);
            var compliesWithProfile = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/structuredefinition-compliesWithProfile");
            CheckRequiresCanonical(pd, resource, "StructureDefinition", compliesWithProfile?.Value, requiresCanonicals);

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
                        CheckRequiresCanonical(pd, resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                    foreach (var binding in t.TargetProfile)
                    {
                        CheckRequiresCanonical(pd, resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                }

                // Terminology Bindings
                CheckRequiresCanonical(pd, resource, "ValueSet", ed.Binding?.ValueSet, requiresCanonicals);
                if (ed.Binding?.Additional != null) // R5 prop name, r4 uses extensions
                {
                    foreach (var binding in ed.Binding?.Additional?.Select(a => a.ValueSet))
                    {
                        CheckRequiresCanonical(pd, resource, "ValueSet", binding, requiresCanonicals);
                    }
                }
				var additionalBindings = ed.Binding?.GetExtensions("http://hl7.org/fhir/tools/StructureDefinition/additional-binding");
				if (additionalBindings != null)
				{
					foreach (var binding in additionalBindings)
					{
						var valueSet = binding.GetExtensionValue<Canonical>("valueSet");
						CheckRequiresCanonical(pd, resource, "ValueSet", valueSet, requiresCanonicals);
					}
				}

				// value Alternatives
				foreach (var alternateExtension in ed.ValueAlternatives)
                {
                    CheckRequiresCanonical(pd, resource, "StructureDefinition", alternateExtension, requiresCanonicals);
                }
            }
        }


        private void ScanForCanonicalsR4(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(pd, requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(pd, resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(pd, requiresCanonicals, resource);

            ScanForCanonicalsR4(pd, requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicalsMetaProfiles(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource)
        {
            if (resource.Meta != null)
            {
                foreach (var profile in resource.Meta?.Profile)
                {
                    CheckRequiresCanonical(pd, resource, "StructureDefinition", profile, requiresCanonicals);
                }
            }
        }

        private void ScanForCanonicalsR4(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(pd, resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "StructureDefinition", item.Definition, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(pd, requiresCanonicals, resource, item);
                ScanForCanonicalsR4(pd, requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(pd, requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(pd, resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(pd, requiresCanonicals, resource);

            ScanForCanonicals(pd, requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4b.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(pd, resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "StructureDefinition", item.Definition, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(pd, requiresCanonicals, resource, item);
                ScanForCanonicals(pd, requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForCanonicalsR5(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.Questionnaire resource)
        {
            ScanForCanonicalsMetaProfiles(pd, requiresCanonicals, resource);

            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(pd, resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(pd, requiresCanonicals, resource);

            ScanForCanonicalsR5(pd, requiresCanonicals, resource, resource.Item);
        }

        private void ScanForCanonicalsR5(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource, List<r5.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(pd, resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
                CheckRequiresCanonical(pd, resource, "StructureDefinition", item.Definition, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(pd, requiresCanonicals, resource, item);
                ScanForCanonicalsR5(pd, requiresCanonicals, resource, item.Item);
            }
        }

        private void ScanForSDCExtensionCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, DomainResource resource)
        {
            // SDC extras
            var tsm = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-targetStructureMap");
            CheckRequiresCanonical(pd, resource, "StructureMap", tsm, requiresCanonicals);
            var qUrl = resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-subQuestionnaire");
            CheckRequiresCanonical(pd, resource, "Questionnaire", qUrl, requiresCanonicals);
        }

        private void ScanForSDCItemExtensionCanonicals(PackageDetails pd, List<CanonicalDetails> requiresCanonicals, Resource resource, Element item)
        {
            // Maybe think some more about if we can dynamically also scan the extensions from the definitions and "discover" more...

            // SDC extras
            var unitValueSetUrl = item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-unitValueSet");
            CheckRequiresCanonical(pd, resource, "ValueSet", unitValueSetUrl, requiresCanonicals);
            var referenceProfile = item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-referenceProfile");
            CheckRequiresCanonical(pd, resource, "StructureDefinition", unitValueSetUrl, requiresCanonicals);
        }
		#endregion

		/// <summary>
		/// Detect any resources required for this package, tagging dependent packages to load their content too
		/// </summary>
		/// <param name="pd"></param>
		/// <param name="versionAgnosticProcessor"></param>
		internal void LoadDependentResources(PackageDetails pd, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			ConsoleEx.WriteLine(ConsoleColor.Gray, $"  Loading package content for {pd.packageId}|{pd.packageVersion}");

			// Determine all the canonicals that are required for this package for the loaded resources
			var scanResources = pd.Files.Where(f => f.resource != null && f.ScannedForDependencies == false).ToList();
			int safetyCatch = 0;
            const int catchLimit = 30;
			while (scanResources.Count > 0 && safetyCatch < catchLimit) // provide a safety net in the event that not all files load
			{
                var newRequiredCanonicals = ScanForCanonicals(pd, scanResources).Except(pd.RequiresCanonicals, new CanonicalDetailsComparer()).ToList();
                var newExternalCanonicals = ExcludeLocalCanonicals(pd, newRequiredCanonicals);
                var newLocalCanonicals = newRequiredCanonicals.Except(newExternalCanonicals);


                LoadCanonicalResource(pd, newLocalCanonicals, versionAgnosticProcessor, errFiles);
                pd.RequiresCanonicals.AddRange(newExternalCanonicals);

			    // Resolve all the required canonicals
			    foreach (var canonical in newExternalCanonicals.Where(r => r.resource == null))
			    {
				    var matches = ResolveCanonical(pd, canonical, versionAgnosticProcessor, errFiles);
				    var useResource = CurrentCanonicalFromPackages.Current(matches);
				    if (useResource != null)
				    {
					    var distinctVersionSources = matches.Select(m => ResourcePackageSource.PackageSourceVersion(m)).Distinct();
					    if (distinctVersionSources.Count() > 1 && _settings.Verbose)
					    {
						    Console.Write($"    Resolved {canonical.Canonical}|{canonical.Version} with ");
						    ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
						    Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
					    }
                        useResource.MarkUsedBy(canonical);
                        canonical.resource = useResource.resource as Resource;
				    }
			    }

                safetyCatch++;

                scanResources = pd.Files.Where(f => f.resource != null && f.ScannedForDependencies == false).ToList();
            }
            if (safetyCatch == catchLimit)
            {
                ConsoleEx.WriteLine(ConsoleColor.Yellow, "Dependency scanning encountered a potential circular dependency");
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

		private static Resource parsePackageContentStream(Common_Processor versionAgnosticProcessor, Stream stream, string filename)
		{
			if (filename.EndsWith(".xml"))
			{
				using (var xr = SerializationUtil.XmlReaderFromStream(stream))
				{
					return versionAgnosticProcessor.ParseXml(xr);
				}
			}
			else if (filename.EndsWith(".json"))
			{
				using (var jr = SerializationUtil.JsonReaderFromStream(stream))
				{
					return versionAgnosticProcessor.ParseJson(jr);
				}
			}
			return null;
		}

		private void LoadCanonicalResource(PackageDetails pd, IEnumerable<CanonicalDetails> canonicals, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			// Tag items that were already loaded due to another parent package
			var itemsToBeTagged = canonicals.Where(cd => cd.resource == null && pd.Files.Any(f => f.url == cd.Canonical
																&& (f.version == cd.Version || string.IsNullOrEmpty(cd.Version))
																&& pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id)));
			foreach (var cd in itemsToBeTagged)
			{
				var possibleFiles = pd.Files.Where(f => f.url == cd.Canonical && (f.version == cd.Version || string.IsNullOrEmpty(cd.Version)));
				var possibleResources = pd.resources.Where(r => possibleFiles.Any(f => r.TypeName == f.resourceType && r.Id == f.id));
				cd.resource = possibleResources.FirstOrDefault();
			}

			// See if there are any remaining items that need loading
			var files = pd.Files.Where(f => canonicals.Any(cd => cd.Canonical == f.url 
																&& (f.version == cd.Version || string.IsNullOrEmpty(cd.Version))
																&& !pd.resources.Any(r => r.TypeName == f.resourceType && r.Id == f.id)
															)).ToList();
			if (files.Count > 0)
			{
				var stream = _packageCache.GetPackageStream(pd.packageId, pd.packageVersion, out var leaveOpen);
				if (stream == null)
				{
					// Need to have a better mechanism here - as can't continue and should just plain barf
					// Console.WriteLine($"Cannot load package {pd.packageId}|{pd.packageVersion}");
					return;
				}
				try
				{
					foreach (var f in files)
					{
						// If the content was already discovered as invalid, no need to try again.
						if (f.detectedInvalidContent)
							continue;

						if (_settings.Verbose)
							Console.WriteLine($"    Detected {f.url}|{f.version} in {pd.packageId}|{pd.packageVersion}");

						if (f.hasDuplicateDefinitions && f.resource == null)
							Console.WriteLine($"    Detected multiple versions of {f.url}|{f.version} in {pd.packageId}|{pd.packageVersion} ({String.Join(", ", files.Where(f2 => f2.url == f.url ).Select(f2 => f2.filename))})");
						try
						{
							var resourceKey = $"{pd.packageId}|{pd.packageVersion}|{f.filename}";
							if (!_cacheResources.TryGetValue(resourceKey, out Resource resource))
							{
								resource = PackageReader.ReadResourceContent(stream, f.filename,
									(stream, filename) => parsePackageContentStream(versionAgnosticProcessor, filename, stream));
								_cacheResources.Add(resourceKey, resource);
							}
							f.resource = resource;
							if (resource == null)
							{
								// Not a file that we can process
								// (What about fml/map files?)
								continue;
							}
                            if (!f.ScannedForDependencies.HasValue)
                                f.ScannedForDependencies = false;
							resource.SetAnnotation(new ResourcePackageSource()
							{
								Filename = f.filename,
								PackageId = pd.packageId,
								PackageVersion = pd.packageVersion
							});

							if (resource is IVersionableConformanceResource ivr)
							{
								foreach (var can in canonicals.Where(c => c.Canonical == ivr.Url))
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
												if (distinctVersionSources.Count() > 1 && _settings.Verbose)
												{
													Console.Write($"    Resolved {can.Canonical}|{can.Version} with ");
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
				finally
				{
					if (!leaveOpen)
						stream.Dispose();
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
		internal IEnumerable<FileDetail> ResolveCanonical(PackageDetails pd, CanonicalDetails canonicalUrl, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			// Is this in any of the dependencies?
			foreach (var dep in pd.dependencies)
			{
				foreach (var resource in ResolveCanonical(dep, canonicalUrl, versionAgnosticProcessor, errFiles))
					yield return resource;
			}

			FileDetail detail = null;
			if (!string.IsNullOrEmpty(canonicalUrl.Version))
			{
				// Check for the versioned canonical
				pd.CanonicalFiles.TryGetValue(canonicalUrl.Canonical + "|" + canonicalUrl.Version, out detail);
			}

			if (detail == null)
			{
				// Check for the un-versioned canonical
				pd.CanonicalFiles.TryGetValue(canonicalUrl.Canonical, out detail);
			}

			if (detail == null || detail.detectedInvalidContent)
				yield break;

			if (detail.resource == null)
			{
				// we need to load it in.
				var stream = _packageCache.GetPackageStream(pd.packageId, pd.packageVersion, out var leaveOpen);
				if (stream == null)
				{
					// No package to retrieve from...
					yield break;
				}

				try
				{
					if (_settings.Verbose)
						Console.WriteLine($"    Detected {detail.url}|{detail.version} in {pd.packageId}|{pd.packageVersion}");
					Resource resource = null;
					try
					{
						var resourceKey = $"{pd.packageId}|{pd.packageVersion}|{detail.filename}";
						if (!_cacheResources.TryGetValue(resourceKey, out resource))
						{
							resource = PackageReader.ReadResourceContent(stream, detail.filename,
								(stream, filename) => parsePackageContentStream(versionAgnosticProcessor, filename, stream));
							_cacheResources.Add(resourceKey, resource);
						}
						detail.resource = resource;
                        if (!detail.ScannedForDependencies.HasValue)
                            detail.ScannedForDependencies = false;
						if (resource == null)
						{
							// Not a file that we can process
							// (What about fml/map files?)
							yield break;
						}
						resource.SetAnnotation(new ResourcePackageSource()
						{
							Filename = detail.filename,
							PackageId = pd.packageId,
							PackageVersion = pd.packageVersion
						});
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine($"ERROR: ({detail.filename}) {ex.Message}");
						//System.Threading.Interlocked.Increment(ref failures);
						//if (!errs.Contains(ex.Message))
						//	errs.Add(ex.Message);
						errFiles.Add(detail.filename);
						detail.detectedInvalidContent = true;
						yield break;
					}
				}
				finally
				{
					if (!leaveOpen)
						stream.Dispose();
				}
			}

			if (detail.resource is IVersionableConformanceResource ivr)
				yield return detail;
		}

		public async Task<List<Resource>> ReadResourcesFromPackage(PackageDetails pd, Func<string, bool> SkipFile, Stream sourceStream, Common_Processor versionAgnosticProcessor, List<string> errs, List<string> errFiles, bool verbose, List<string> resourceTypeNames, Program.Result result)
		{
			// skip back to the start (for cases where the package.json isn't the first resource)
			sourceStream.Seek(0, SeekOrigin.Begin);
			Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress, true);
			MemoryStream ms = new MemoryStream();
			using (gzipStream)
			{
				// Unzip the tar file into a memory stream
				await gzipStream.CopyToAsync(ms);
				ms.Seek(0, SeekOrigin.Begin);
			}
			var reader = new TarReader(ms);

			List<Resource> resourcesToProcess = new();
			TarEntry entry;
			while ((entry = reader.GetNextEntry()) != null)
			{
				if (SkipFile(entry.Name))
					continue;
				if (entry.EntryType != TarEntryType.Directory)
				{
					var exampleName = entry.Name;
					if (verbose)
						Console.WriteLine($"Processing: {exampleName}");
					var stream = entry.DataStream;
					using (stream)
					{
						Resource resource = null;
						try
						{
							resource = parsePackageContentStream(versionAgnosticProcessor, stream, exampleName);
							if (resource == null)
							{
								// Not a file that we can process
								// (What about fml/map files?)
								continue;
							}
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"ERROR: ({exampleName}) {ex.Message}");
							System.Threading.Interlocked.Increment(ref result.failures);
							if (!errs.Contains(ex.Message))
								errs.Add(ex.Message);
							errFiles.Add(exampleName);
							continue;
						}

						// Skip resource types we're not intentionally importing
						// (usually examples)
						if (resourceTypeNames.Count > 0 && !resourceTypeNames.Contains(resource.TypeName))
						{
							if (verbose)
								Console.WriteLine($"    ----> Ignoring {exampleName} because {resource.TypeName} is not a requested type");
							continue;
						}

						resourcesToProcess.Add(resource);
						resource.SetAnnotation(new ResourcePackageSource()
						{
							Filename = exampleName,
							PackageId = pd.packageId,
							PackageVersion = pd.packageVersion
						});

						FileDetail indexDetails = pd.Files.FirstOrDefault(f => "package/" + f.filename == exampleName);
						if (indexDetails == null)
						{
							indexDetails = new FileDetail()
							{
								filename = exampleName,
								resourceType = resource.TypeName,
								id = resource.Id,
							};
							if (resource is IVersionableConformanceResource vcr)
							{
								indexDetails.url = vcr.Url;
								indexDetails.version = vcr.Version;
								pd.CanonicalFiles.Add($"{vcr.Url}|{vcr.Version}", indexDetails);
								if (!pd.CanonicalFiles.ContainsKey(vcr.Url))
									pd.CanonicalFiles.Add(vcr.Url, indexDetails);
							}
							pd.Files.Add(indexDetails);
						}
                        indexDetails.UsedBy.Add("(root package)");
						indexDetails.resource = resource;
                        if (!indexDetails.ScannedForDependencies.HasValue)
                            indexDetails.ScannedForDependencies = false;
					}
				}
			}
			return resourcesToProcess;
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

						var cd = pd.RequiresCanonicals.FirstOrDefault(c => c.Canonical == canonical.Value && (string.IsNullOrEmpty(c.Version) || string.IsNullOrEmpty(canonical.Version) || c.Version == canonical.Version));
						if (cd != null && cd.resource is IVersionableConformanceResource ivr)
						{
							if (string.IsNullOrEmpty(cd.Version))
								cd.Version = ivr.Version;
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
							var cd = pd.RequiresCanonicals.FirstOrDefault(c => c.Canonical == include.System);
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
