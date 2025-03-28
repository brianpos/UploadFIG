extern alias r4;
extern alias r4b;
extern alias r5;
namespace UploadFIG
{
    /// <summary>
    /// The resource containing this annotation has a dependency with the canonical in this Value
    /// </summary>
    public record DependsOnCanonical
    {
        public DependsOnCanonical(string value)
        {
            CanonicalUrl = value;
        }

        public string CanonicalUrl { get; init; }
    }
}
