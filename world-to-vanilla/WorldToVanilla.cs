using Terraria;
using Terraria.ModLoader;

namespace WorldToVanilla;

public sealed class WorldToVanilla : Mod
{
	public override void Load()
	{
		if (!Main.dedServ) {
			WorldExportUi.Load(this);
		}
	}

	public override void Unload()
	{
		if (!Main.dedServ) {
			WorldExportUi.Unload();
		}
	}
}
