# Notices

Terraria and related names are trademarks and copyrighted works of Re-Logic.
Terraria Seed Roller is an independent tool and is not affiliated with or
endorsed by Re-Logic. No Terraria source code, executable, texture, audio, or
other game asset is distributed by this project. Users must provide their own
lawfully installed copy of Terraria and TerrariaServer.

Mechanism documentation links to the community-maintained official Terraria
Wiki at terraria.wiki.gg. Research repositories used during development are not
vendored, built, copied into releases, or included in this repository.

## Third-party assets

The GUI icon set is derived from [Lucide](https://lucide.dev) v1.47.0, which is
licensed under the ISC License:

> ISC License
>
> Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part
> of Feather Icons (MIT), and for the other portions of Lucide are held by
> Lucide Contributors 2022.
>
> Permission to use, copy, modify, and/or distribute this software for any
> purpose with or without fee is hereby granted, provided that the above
> copyright notice and this permission notice appear in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
> REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
> AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
> INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
> LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
> OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
> PERFORMANCE OF THIS SOFTWARE.

The SVG sources are kept in `scripts/icons/`, and `scripts/generate-icons.ps1`
converts them into the vector path data compiled into the application
(`src/TerrariaSeedRoller.App/Design/IconData.cs`). No raster image assets are
shipped. All colours, layout, and control rendering are original to this
project.
