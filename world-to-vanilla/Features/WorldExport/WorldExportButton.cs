using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace WorldToVanilla;

internal sealed class WorldExportButton : UIImageButton
{
	private const int BadgeSize = 10;
	private string _tooltipText;
	private WorldExportState _state;

	public WorldExportButton(
		Asset<Texture2D> texture,
		WorldExportState state,
		string tooltipText)
		: base(texture)
	{
		_tooltipText = tooltipText;
		SetState(state, tooltipText);
	}

	public void SetState(WorldExportState state, string tooltipText)
	{
		_state = state;
		_tooltipText = tooltipText;

		float inactiveVisibility = state is WorldExportState.Outdated
			or WorldExportState.VanillaNewer
			or WorldExportState.Unavailable
			? 0.9f
			: 0.7f;
		SetVisibility(1f, inactiveVisibility);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		base.DrawSelf(spriteBatch);
		DrawStatusBadge(spriteBatch);

		if (IsMouseHovering) {
			UICommon.TooltipMouseText(_tooltipText);
		}
	}

	private void DrawStatusBadge(SpriteBatch spriteBatch)
	{
		CalculatedStyle dimensions = GetDimensions();
		int x = (int)(dimensions.X + dimensions.Width) - BadgeSize + 2;
		int y = (int)dimensions.Y - 2;
		Texture2D pixel = TextureAssets.MagicPixel.Value;

		DrawPixelRectangle(spriteBatch, pixel, x, y, BadgeSize, BadgeSize, new Color(15, 18, 28));
		DrawPixelRectangle(spriteBatch, pixel, x + 1, y + 1, BadgeSize - 2, BadgeSize - 2, GetBadgeColor());

		switch (_state) {
			case WorldExportState.NotExported:
				DrawPixelRectangle(spriteBatch, pixel, x + 3, y + 4, 4, 2, Color.White);
				break;
			case WorldExportState.UpToDate:
				DrawCheck(spriteBatch, pixel, x, y);
				break;
			case WorldExportState.Outdated:
				DrawPixelRectangle(spriteBatch, pixel, x + 4, y + 2, 2, 4, Color.White);
				DrawPixelRectangle(spriteBatch, pixel, x + 4, y + 7, 2, 1, Color.White);
				break;
			case WorldExportState.VanillaNewer:
				DrawDownArrow(spriteBatch, pixel, x, y);
				break;
			case WorldExportState.Unavailable:
				DrawCross(spriteBatch, pixel, x, y);
				break;
		}
	}

	private Color GetBadgeColor()
	{
		return _state switch {
			WorldExportState.NotExported => new Color(102, 110, 134),
			WorldExportState.UpToDate => new Color(43, 158, 79),
			WorldExportState.Outdated => new Color(222, 143, 28),
			WorldExportState.VanillaNewer => new Color(48, 126, 214),
			WorldExportState.Unavailable => new Color(196, 55, 55),
			_ => Color.White
		};
	}

	private static void DrawCheck(SpriteBatch spriteBatch, Texture2D pixel, int x, int y)
	{
		DrawPixelRectangle(spriteBatch, pixel, x + 2, y + 5, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 4, y + 6, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 5, y + 4, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 6, y + 2, 2, 3, Color.White);
	}

	private static void DrawDownArrow(SpriteBatch spriteBatch, Texture2D pixel, int x, int y)
	{
		DrawPixelRectangle(spriteBatch, pixel, x + 4, y + 2, 2, 4, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 2, y + 5, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 6, y + 5, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 3, y + 7, 4, 1, Color.White);
	}

	private static void DrawCross(SpriteBatch spriteBatch, Texture2D pixel, int x, int y)
	{
		DrawPixelRectangle(spriteBatch, pixel, x + 2, y + 2, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 6, y + 2, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 4, y + 4, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 2, y + 6, 2, 2, Color.White);
		DrawPixelRectangle(spriteBatch, pixel, x + 6, y + 6, 2, 2, Color.White);
	}

	private static void DrawPixelRectangle(
		SpriteBatch spriteBatch,
		Texture2D pixel,
		int x,
		int y,
		int width,
		int height,
		Color color)
	{
		spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), color);
	}
}
