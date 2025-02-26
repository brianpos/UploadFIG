extern alias r4;
extern alias r4b;
extern alias r5;
namespace UploadFIG
{
	public record DependsOnCanonical
	{
		public DependsOnCanonical(string value)
		{
			CanonicalUrl = value;
		}

		public string CanonicalUrl { get; init; }
	}
}
