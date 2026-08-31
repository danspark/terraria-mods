using Microsoft.Xna.Framework;
using RicherBiomes.Systems;
using Terraria;
using Terraria.ModLoader;

namespace RicherBiomes.Players;

public sealed class RicherBiomesPlayer : ModPlayer
{
	public override void OnEnterWorld()
	{
		ActiveFeatureInfo? feature = RicherBiomesWorldSystem.ActiveFeature;
		if (feature is null) {
			return;
		}

		Main.NewText(
			$"Richer Biomes begins about {feature.StartDistance} tiles {feature.DirectionName} of spawn. " +
			"Follow the layered forest to the mountain crossing; the surface mine lies beyond the far slope.",
			new Color(125, 220, 135));
	}
}
