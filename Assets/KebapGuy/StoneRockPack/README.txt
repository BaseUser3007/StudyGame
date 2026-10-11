STONE & ROCK PACK  (v1.0)  by Kebap Guy  -  Documentation
57 stone & rock designs - 3 colors - snow versions - 2 sizes
pixel art for 16x16 tile grids: top-down RPGs, farming sims and adventure games

Requires Unity 2022.3 or newer.

============================================================
QUICK START
============================================================
1. Open Demo/Demo.unity to see every design in every color.
2. Drag any sprite from Sprites/<Color> into your scene.

============================================================
CONTENTS
============================================================
684 sprites = 57 designs x 2 sizes x 6 color variants

Sprites/Gray          cool blue-grey stone          114 sprites
Sprites/Brown         warm brown stone              114 sprites
Sprites/Moss          grey stone with green moss    114 sprites
Sprites/Gray_Snow     grey stone, snow-covered      114 sprites
Sprites/Brown_Snow    brown stone, snow-covered     114 sprites
Sprites/Moss_Snow     mossy stone, snow-covered     114 sprites
Demo/Demo.unity       demo scene

Shapes (19 shapes, several different designs of each).
File name prefix, description, number of designs, size (small size):
  STONES - small ground decoration
    stone_round    Round stone            x2   32x32  (small 16x16)
    stone_pointy   Pointy stone           x2   32x32  (small 16x16)
    stone_cluster  Small stone cluster    x4   32x32  (small 16x16)
    stone_flat     Flat stone             x3   32x32  (small 16x16)
    stone_pebbles  Scattered pebbles      x3   32x32  (small 16x16)
  ROCKS - boulders
    rock_round     Round boulder          x3   48x48  (small 32x32)
    rock_angular   Angular boulder        x4   48x48  (small 32x32)
    rock_cracked   Cracked boulder        x2   48x48  (small 32x32)
    rock_cluster   Boulder cluster        x3   48x48  (small 32x32)
    rock_giant     Giant boulder          x4   64x64  (small 32x32)
  LANDMARKS - arches, stone gates, cairns, rock piles, spires
    rock_arch      Natural rock arch      x5   64x48  (small 32x32)
    rock_gate      Stone gate (dolmen)    x2   64x48  (small 32x32)
    rock_cairn     Balanced stone cairn   x4   32x48  (small 16x32)
    rock_pile      Rock pile              x4   64x64  (small 32x32)
    rock_spire     Rock spire             x3   32x64  (small 16x32)
  RUINS (bonus) - paving slabs, stone columns, ruined gate and arch
    ruin_slab      Stone paving slab      x3   32x32  (small 16x16)
    ruin_column    Stone column           x4   32x64  (small 16x32)
    ruin_gate      Ruined stone gate      x1   64x48  (small 32x32)
    ruin_arch      Ruined stone arch      x1   64x48  (small 32x32)

============================================================
FILE NAMES
============================================================
  <shape>_<design>_<color>.png           rock_arch_02_moss.png
  <shape>_<design>_small_<color>.png     rock_arch_02_small_moss_snow.png

Every design exists in all 6 colors and in 2 sizes. The "small" version
is the same design at half size with its own outline, not a blurry
downscale.

============================================================
IMPORT SETTINGS (already set up)
============================================================
- Pixels Per Unit = 16: one 16x16 tile is 1 Unity unit.
- Filter Mode = Point, Compression = None, no mipmaps: pixels stay sharp.
- Pivot = Bottom Center: every sprite is bottom-aligned and horizontally
  centered on its canvas, so objects line up on your tile grid.
- Every canvas is a multiple of 16:
    16x16 = 1x1 tile (small stones)     32x32 = 2x2 tiles
    48x48 = 3x3 tiles (boulders)        64x64 = 4x4 tiles (giant boulder, rock pile)
    64x48 = 4x3 tiles (arches, gates)   32x64 = 2x4 tiles (spires, columns)
- PNG with true alpha transparency, no semi-transparent pixels.
- All sprites share one limited 25-color palette, light comes from the
  top left and every sprite has a dark outline.
- Use the sprites at their original size - don't scale them down.

============================================================
RENDER PIPELINES
============================================================
These are plain sprites with no custom materials or shaders, so they work
in the Built-in Render Pipeline and in URP (2D Renderer and Universal
Renderer). The demo scene uses the Sprites-Default material.
Using URP 2D lights? Set the Sprite Renderer material to
Sprite-Lit-Default so the rocks react to your lights.

============================================================
LICENSE
============================================================
Licensed under the Standard Unity Asset Store EULA - see LICENSE.txt.

============================================================
CREDITS
============================================================
Created with PixelLab (AI image generation), then cleaned up and recolored
(brown, moss, snow) with a custom pixel-art pipeline and edits in Aseprite.

Questions or feedback? Contact Kebap Guy through the publisher page
on the Unity Asset Store.
