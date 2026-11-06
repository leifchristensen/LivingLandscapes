using System.IO;
using System.Linq;
using System.Xml.Linq;
using LivingLandscapes.Flora;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// #166: a plant's shade radius must not depend on how a visual mod draws it (Better Trees doubles every tree's
/// visualSizeRange). The radius takes no size input at all; these tests pin that and that every shading canopy plant of
/// the habitat patch states its crown.
/// </summary>
public class ShadeRadiusTests
{
    [Fact]
    public void ExplicitRadiusWins()
    {
        Assert.Equal(1.8f, ShadeRadiusRules.Radius(1.8f, canopy: true));
        Assert.Equal(1.8f, ShadeRadiusRules.Radius(1.8f, canopy: false));
    }

    [Fact]
    public void UndescribedPlantTakesItsStratumsCrown()
    {
        Assert.Equal(ShadeRadiusRules.CanopyDefault, ShadeRadiusRules.Radius(0f, canopy: true));
        Assert.Equal(ShadeRadiusRules.UnderstoryDefault, ShadeRadiusRules.Radius(0f, canopy: false));
        Assert.True(ShadeRadiusRules.CanopyDefault > ShadeRadiusRules.UnderstoryDefault);
    }

    /// <summary>Every habitat that casts shade says how far: otherwise a fallback decides, and fallbacks are for mods we have not described.</summary>
    [Fact]
    public void EveryShadingHabitatInThePatchStatesItsRadius()
    {
        XDocument doc = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "PlantHabitats.xml"));
        var missing = doc.Descendants("li")
            .Where(li => (string)li.Attribute("Class") == "LivingLandscapes.Flora.PlantHabitat")
            .Where(li => (float?)li.Element("shadeCast") > 0f && !((float?)li.Element("shadeRadius") > 0f))
            .Select(li => (string)li.Ancestors("match").First().Element("xpath"))
            .ToList();
        Assert.True(missing.Count == 0, "habitats with shadeCast but no shadeRadius: " + string.Join(", ", missing));
    }
}
