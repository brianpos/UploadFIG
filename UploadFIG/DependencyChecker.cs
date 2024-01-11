extern alias r4;
extern alias r4b;
extern alias r5;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Utility;

namespace UploadFIG
{

    internal static class DependencyChecker
    {
        public static void VerifyDependenciesOnServer(Settings settings, BaseFhirClient clientFhir, List<CanonicalDetails> requiresCanonicals)
        {
            if (settings.TestPackageOnly)
                return;

            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            var oldColor = Console.ForegroundColor;
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
                if (existing == null || existing.Entry.Count(e => !(e.Resource is OperationOutcome)) > 0)
                {
                    var versionList = existing.Entry.Select(e => (e.Resource as IVersionableConformanceResource)?.Version).ToList();
                    if (settings.PreventDuplicateCanonicalVersions && versionList.Count > 1)
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t{string.Join(", ", versionList)}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t(missing)");
                }
                Console.ForegroundColor = oldColor;
            }
        }

        /// <summary>
        /// Scan the provided set of resources and return any canonicals that are referenced by the resources.
        /// </summary>
        /// <param name="resourcesToProcess"></param>
        /// <returns></returns>
        public static IEnumerable<CanonicalDetails> ScanForCanonicals(List<Resource> resourcesToProcess)
        {
            return ScanForCanonicals(new List<CanonicalDetails>(), resourcesToProcess);
		}

        /// <summary>
        /// Scan the provided set of resources and return any canonicals referenced by the resources that are not already in the initialCanonicals list.
        /// </summary>
        /// <param name="initialCanonicals"></param>
        /// <param name="resourcesToProcess"></param>
        /// <returns></returns>
		public static IEnumerable<CanonicalDetails> ScanForCanonicals(IEnumerable<CanonicalDetails> initialCanonicals, List<Resource> resourcesToProcess)
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
			return requiresCanonicals.Where(r => !initialCanonicals.Any(ic => ic.canonical == r.canonical));

		}

		private static void ScanForExtensions(List<CanonicalDetails> requiresCanonicals, Resource resource, Base prop)
		{
			foreach (var child in prop.Children)
			{
                if (child is Extension extension)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", extension.Url, requiresCanonicals);
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
		public static IEnumerable<CanonicalDetails> FilterOutCanonicals(IEnumerable<CanonicalDetails> initialCanonicals, List<Resource> excludeResources)
		{
			List<CanonicalDetails> filteredCanonicals = new List<CanonicalDetails>(initialCanonicals);

			// Now check for the ones that we've internally got covered :)
			foreach (var resource in excludeResources.OfType<IVersionableConformanceResource>())
			{
				var node = initialCanonicals.FirstOrDefault(rc => rc.resourceType == (resource as Resource).TypeName && rc.canonical == resource.Url);
				if (node != null)
				{
					filteredCanonicals.Remove(node);
				}
			}

            return filteredCanonicals;
		}

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
		public static IEnumerable<CanonicalDetails> FilterOutCoreSpecAndExtensionCanonicals(IEnumerable<CanonicalDetails> initialCanonicals, FHIRVersion fhirversion, Common_Processor versionAgnosticProcessor)
		{
			List<CanonicalDetails> filteredCanonicals = new List<CanonicalDetails>(initialCanonicals);

			// And the types from the core resource profiles
			var coreCanonicals = initialCanonicals.Where(v => Uri.IsWellFormedUriString(v.canonical, UriKind.Absolute) && versionAgnosticProcessor.ModelInspector.IsCoreModelTypeUri(new Uri(v.canonical))).ToList();
			foreach (var coreCanonical in coreCanonicals)
			{
				filteredCanonicals.Remove(coreCanonical);
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
				return filteredCanonicals;
			}
			// ensure that the zip file is extracted correctly before using it
			zipSource.Prepare();

			// Scan for core/core extensions dependencies
			var coreSource = new CachedResolver(zipSource);
			var extensionCanonicals = initialCanonicals.Where(v => coreSource.ResolveByCanonicalUri(v.canonical) != null).ToList();
			foreach (var coreCanonical in extensionCanonicals)
			{
				filteredCanonicals.Remove(coreCanonical);
			}
			return filteredCanonicals;
		}

		public static void ExcludeKnownCanonicals(List<CanonicalDetails> requiresCanonicals, FHIRVersion fhirversion, List<Resource> resourcesToProcess, Common_Processor versionAgnosticProcessor, InMemoryResolver inMemoryResolver)
		{
			List<CanonicalDetails> allRequiredCanonicals = new List<CanonicalDetails>(requiresCanonicals);

			// Now check for the ones that we've internally got covered :)
			foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>())
			{
				var node = requiresCanonicals.FirstOrDefault(rc => rc.resourceType == (resource as Resource).TypeName && rc.canonical == resource.Url);
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

        /// <summary>
        /// Recursively scan throught the list of canonicals and load the resources, and then scan for any dependencies of those resources
        /// </summary>
        /// <param name="knownCanonicals">Should not contain any core or extension canonicals</param>
        /// <param name="inMemoryResolver"></param>
        /// <returns></returns>
		public static IEnumerable<CanonicalDetails> RecurseDependencies(IEnumerable<CanonicalDetails> knownCanonicals, InMemoryResolver inMemoryResolver, FHIRVersion fhirversion, Common_Processor versionAgnosticProcessor)
		{
            List<CanonicalDetails> allRequiredCanonicals = new List<CanonicalDetails>(knownCanonicals);
            List<CanonicalDetails> unresolvableCanonicals = new List<CanonicalDetails>();

			// scan through this list and resolve the resources not already resolved.
            List<Resource> additionalCanonicalResources = new List<Resource>();
            foreach (var canonical in knownCanonicals.Where(cd => cd.resource == null))
            {
                var item = inMemoryResolver.ResolveByCanonicalUri(canonical.canonical);
                if (item != null)
                {
					canonical.resource = item;
					additionalCanonicalResources.Add(item);
				}
                else
                {
					unresolvableCanonicals.Add(canonical);
				}
            }

            var extraCanonicals = ScanForCanonicals(allRequiredCanonicals, additionalCanonicalResources);
            extraCanonicals = FilterOutCoreSpecAndExtensionCanonicals(extraCanonicals, fhirversion, versionAgnosticProcessor);
            if (extraCanonicals.Any())
            {
                allRequiredCanonicals.AddRange(extraCanonicals);
                var resultingCanonicals = RecurseDependencies(allRequiredCanonicals.Except(unresolvableCanonicals), inMemoryResolver, fhirversion, versionAgnosticProcessor);
                return resultingCanonicals.Union(allRequiredCanonicals).Where(r => !knownCanonicals.Any(kc => kc.canonical == r.canonical));
            }

            return allRequiredCanonicals.Where(r => !knownCanonicals.Any(kc => kc.canonical == r.canonical));
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
			"http://hl7.org/fhir/StructureDefinition/structuredefinition-conformance-derivedFrom",
			"http://hl7.org/fhir/StructureDefinition/elementdefinition-type-must-support",
		});

        private static void CheckRequiresCanonical(Resource resource, string canonicalType, string canonicalUrl, List<CanonicalDetails> requiresCanonicals)
        {
            if (ignoreCanonicals.Contains(canonicalUrl))
                return;

            if (!string.IsNullOrEmpty(canonicalUrl))
            {
                if (canonicalUrl.StartsWith("#") && resource is DomainResource dr)
                {
                    // local reference - check that it exists in the resource
                    var localRef = dr.Contained?.Where(c => c.Id == canonicalUrl.Substring(1));
                    if (!localRef.Any())
                        Console.WriteLine($"Unable to resolve contained canonical in {resource.TypeName}/{resource.Id}: {canonicalUrl}");
                    return;
                }

                Canonical c = new Canonical(canonicalUrl);
                var usedBy = requiresCanonicals.Where(s => s.canonical == c.Uri && s.resourceType == canonicalType);
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

        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.ConceptMap resource)
        {
            foreach (var prop in resource.Property)
            {
                CheckRequiresCanonical(resource, "CodeSystem", prop.System, requiresCanonicals);
            }
            CheckRequiresCanonical(resource, "ValueSet", resource.SourceScope as Canonical ?? (resource.SourceScope as FhirUri)?.Value, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.TargetScope as Canonical ?? (resource.TargetScope as FhirUri)?.Value, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    CheckRequiresCanonical(resource, "ValueSet", element.ValueSet, requiresCanonicals);

                    foreach (var target in element.Target)
                    {
                        CheckRequiresCanonical(resource, "ValueSet", target.ValueSet, requiresCanonicals);
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", dependsOn.ValueSet, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", product.ValueSet, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ValueSet", group.Unmapped.ValueSet, requiresCanonicals);
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.OtherMap, requiresCanonicals);
                }
            }
        }


        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(resource, "CodeSystem", resource.Supplements, requiresCanonicals);
            // Removing this check for the "complete valueset" reference as this is quite often not there
            // and if others need it, they would have a reference to it.
            // CheckRequiresCanonical(resource, "ValueSet", resource.ValueSet, requiresCanonicals);
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, ValueSet resource)
        {
            foreach (var include in resource?.Compose?.Include)
            {
                CheckRequiresCanonical(resource, "CodeSystem", include.System, requiresCanonicals);
                foreach (var binding in include.ValueSet)
                {
                    CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                }
            }
            foreach (var exclude in resource?.Compose?.Exclude)
            {
                CheckRequiresCanonical(resource, "CodeSystem", exclude.System, requiresCanonicals);
                foreach (var binding in exclude.ValueSet)
                {
                    CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, StructureDefinition resource)
        {
			CheckRequiresCanonical(resource, "StructureDefinition", resource.BaseDefinition, requiresCanonicals);
			
            if (resource?.Differential?.Element == null)
            {
                // Nothing to process
                return;
            }

            foreach (var ed in resource?.Differential?.Element)
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
                if (ed.Binding?.Additional != null)
                {
                    foreach (var binding in ed.Binding?.Additional?.Select(a => a.ValueSet))
                    {
                        CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                    }
                }

                // value Alternatives
                foreach (var alternateExtension in ed.ValueAlternatives)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", alternateExtension, requiresCanonicals);
                }
            }
        }


        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.Questionnaire resource)
        {
			ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

			foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR4(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicalsMetaProfiles(List<CanonicalDetails> requiresCanonicals, Resource resource)
        {
            if (resource.Meta != null)
            {
                foreach (var profile in resource.Meta?.Profile)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", profile, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
				CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals);

				ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR4(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.Questionnaire resource)
        {
			ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

			foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicals(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4b.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
				CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals);

				ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicals(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.Questionnaire resource)
        {
			ScanForCanonicalsMetaProfiles(requiresCanonicals, resource);

			foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR5(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r5.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);
				CheckRequiresCanonical(resource, "StructureDefinition", item.Definition, requiresCanonicals);

				ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR5(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForSDCExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, DomainResource resource)
        {
            // SDC extras
            CheckRequiresCanonical(resource, "StructureMap", resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-targetStructureMap"), requiresCanonicals);
            CheckRequiresCanonical(resource, "Questionnaire", resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-subQuestionnaire"), requiresCanonicals);
        }

        private static void ScanForSDCItemExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, Element item)
        {
            // Maybe think some more about if we can dynamically also scan the extensions from the definitions and "discover" more...

            // SDC extras
            CheckRequiresCanonical(resource, "ValueSet", item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-unitValueSet"), requiresCanonicals);
            CheckRequiresCanonical(resource, "StructureDefinition", item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-referenceProfile"), requiresCanonicals);
        }
    }
}
