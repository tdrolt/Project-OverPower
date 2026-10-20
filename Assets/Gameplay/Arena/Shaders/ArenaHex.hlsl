#ifndef OVERPOWER_ARENA_HEX_INCLUDED
#define OVERPOWER_ARENA_HEX_INCLUDED

// The arena's look, pushed by ArenaShaderGlobals as globals: outside UnityPerMaterial, so the SRP Batcher and Batching Static
// both survive. Everything is world position, world normal and UV0 (no object space: the arena is statically batched).
float4 _ArenaSurfaceBase[4];   // linear base tint: floor, wall, block, barrier
float  _ArenaSurfaceGloss[4];
float  _ArenaSurfaceShine[4];  // 0..1 share of the main light's specular highlight kept, per surface
float4 _ArenaFloorLine;        // line width m, outside grid glow, owned edge glow, owned fill glow
float4 _ArenaStripe;           // circuit cell m, stripe width m, node size m, top edge width m
float4 _ArenaStripeGlow;       // outside stripe glow, owned stripe glow (ArenaLook.stripeGlow), across chance, up chance
float4 _ArenaOutsideLine;      // linear colour of the faint glow away from every zone
float  _ArenaReflection;       // 0..1 share of URP's sky reflection kept
float  _ArenaLookReady;        // 0 until ArenaLook was pushed: draw the material's own Lit values
float4 _ArenaOwnerGrid;        // origin x, origin z, tile size, 1 while owner tiles are bound
float4 _ArenaOwnerMin;         // lowest q, lowest r, texture width, texture height
TEXTURE2D(_ArenaOwnerTex);
// Never set as a global: only a placed cover wall's property block fills it (CoverPanelLook), so the static arena reads 0 and draws as before.
float4 _ArenaPanelEdge;        // linear owner colour x glow, band width m (0 = no band)

#define ARENA_SQRT3 1.7320508

// --- Hex grid: HexGrid.cs line for line (pointy toward +Z, size = distance between neighbouring centres). ---

float2 ArenaHexRound(float2 qr)
{
    float3 c = float3(qr.x, qr.y, -qr.x - qr.y);
    float3 r = round(c);
    float3 d = abs(r - c);
    if (d.x > d.y && d.x > d.z) r.x = -r.y - r.z;
    else if (d.y > d.z) r.y = -r.x - r.z;
    return r.xy;
}

float2 ArenaTileAt(float2 xz, float2 origin, float size)
{
    float2 p = (xz - origin) / size;
    return ArenaHexRound(float2(p.x - p.y / ARENA_SQRT3, 2.0 * p.y / ARENA_SQRT3));
}

float2 ArenaCentreOf(float2 axial, float2 origin, float size)
{
    return origin + size * float2(axial.x + 0.5 * axial.y, 0.5 * ARENA_SQRT3 * axial.y);
}

// --- Owner tiles (OwnerTileTexel): alpha 0 plain, 64 neutral (unlit lines in the texel colour), 128..255 owned with fill 0..1;
// 40 and 104 are neutral and owned for the walls only (a tile whose centre is past the arena's outline): the floor draws those plain. ---

struct ArenaOwner
{
    float state;   // 0 outside every band, 1 neutral, 2 owned
    float3 colour; // linear
    float fill;
    bool wallsOnly;
};

ArenaOwner ArenaOwnerAt(float2 xz)
{
    ArenaOwner o;
    o.state = 0; o.colour = 0; o.fill = 0; o.wallsOnly = false;
    if (_ArenaOwnerGrid.w < 0.5)
        return o; // never sample unbound tiles (lobby, Edit Mode)
    int2 texel = int2(ArenaTileAt(xz, _ArenaOwnerGrid.xy, _ArenaOwnerGrid.z) - _ArenaOwnerMin.xy);
    if (texel.x < 0 || texel.y < 0 || texel.x >= (int)_ArenaOwnerMin.z || texel.y >= (int)_ArenaOwnerMin.w)
        return o;
    float4 t = LOAD_TEXTURE2D(_ArenaOwnerTex, texel);
    float a = t.a * 255.0;
    if (a < 32.0)
        return o;
    o.colour = t.rgb;
    o.state = a < 96.0 ? 1 : 2;
    o.fill = saturate((a - 128.0) / 127.0);
    o.wallsOnly = a < 56.0 || (a >= 96.0 && a < 120.0);
    return o;
}

// --- Line drawing in metres: antialiased over aa (metres per pixel, taken before any branch), and fading out where a line is
// thinner than a pixel so it never shimmers. ---

float ArenaLineMask(float dist, float width, float aa)
{
    return saturate((0.5 * width - dist) / aa + 0.5) * saturate(width / aa);
}

float ArenaSegmentDist(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// Dave Hoskins' hash without sine: the same on every GPU precision we ship.
float ArenaHash(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

// Circuit cells in face metres. Each cell edge is crossed (or not) by a hashed flag the two neighbouring cells share, so every
// stripe continues into the next cell. Two adjacent crossings join with a 45-degree segment; opposite ones run straight; one
// crossing ends in a square node; three or four meet at a node. Returns the stripe coverage 0..1.
float ArenaCircuit(float2 uv, float aa)
{
    float cell = max(_ArenaStripe.x, 0.05);
    float2 id = floor(uv / cell);
    float2 p = uv - id * cell;
    float h = 0.5 * cell;
    float across = _ArenaStripeGlow.z, up = _ArenaStripeGlow.w;
    float l = ArenaHash(float3(id.x, id.y, 1.0)) < across ? 1 : 0;
    float r = ArenaHash(float3(id.x + 1.0, id.y, 1.0)) < across ? 1 : 0;
    float b = ArenaHash(float3(id.x, id.y, 2.0)) < up ? 1 : 0;
    float t = ArenaHash(float3(id.x, id.y + 1.0, 2.0)) < up ? 1 : 0;
    float n = l + r + b + t;

    float2 c = float2(h, h);
    float2 mL = float2(0, h), mR = float2(cell, h), mB = float2(h, 0), mT = float2(h, cell);
    float d = 1e4;
    if (n == 2 && l != r)
    {
        // a turn: straight across the corner between the two edge midpoints, which is exactly 45 degrees
        float2 a = l > 0 ? mL : mR;
        float2 e = b > 0 ? mB : mT;
        d = ArenaSegmentDist(p, a, e);
    }
    else
    {
        if (l > 0) d = min(d, ArenaSegmentDist(p, mL, c));
        if (r > 0) d = min(d, ArenaSegmentDist(p, mR, c));
        if (b > 0) d = min(d, ArenaSegmentDist(p, mB, c));
        if (t > 0) d = min(d, ArenaSegmentDist(p, mT, c));
    }
    float stripe = n > 0 ? ArenaLineMask(d, _ArenaStripe.y, aa) : 0;
    float2 q = abs(p - c);
    float node = saturate((0.5 * _ArenaStripe.z - max(q.x, q.y)) / aa + 0.5);
    return (n == 1 || n >= 3) ? max(stripe, node) : stripe;
}

// Metres from the nearest edge of a face, from UV0 (the arena's cubes map every face 0..1). The UV-to-world scale comes from
// screen derivatives, so a stretched cube still gets a border of the same width in metres on every side.
float ArenaFaceEdgeMetres(float2 uv, float3 positionWS)
{
    float2 duvdx = ddx(uv), duvdy = ddy(uv);
    float3 dpdx = ddx(positionWS), dpdy = ddy(positionWS);
    float det = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
    if (abs(det) < 1e-12)
        return 1e4;
    float metresPerU = length((dpdx * duvdy.y - dpdy * duvdx.y) / det);
    float metresPerV = length((dpdy * duvdx.x - dpdx * duvdy.x) / det);
    float2 e = min(uv, 1.0 - uv);
    return min(e.x * metresPerU, e.y * metresPerV);
}

// Paints the pattern of one surface onto Lit's surface data. Lines in an owned band glow in the owner's colour; in a neutral
// band they are unlit lighter lines (lit vs. unlit, not brightness, tells neutral from the White team); elsewhere a faint cool glow.
void ArenaPaint(int surface, float3 positionWS, float3 normalWS, float2 uv, inout SurfaceData s)
{
    s.albedo = _ArenaSurfaceBase[surface].rgb;
    s.smoothness = _ArenaSurfaceGloss[surface];
    float aa = max(length(fwidth(positionWS)) * 0.7, 1e-4);

#if defined(_ARENA_FLOOR)
    float size = max(_ArenaOwnerGrid.z, 0.01);
    float2 centre = ArenaCentreOf(ArenaTileAt(positionWS.xz, _ArenaOwnerGrid.xy, size), _ArenaOwnerGrid.xy, size);
    float2 p = positionWS.xz - centre;
    float hd = max(abs(p.x), max(abs(0.5 * p.x + 0.8660254 * p.y), abs(-0.5 * p.x + 0.8660254 * p.y)));
    float edges = normalWS.y > 0.5 ? ArenaLineMask(0.5 * size - hd, _ArenaFloorLine.x, aa) : 0;
    ArenaOwner o = ArenaOwnerAt(positionWS.xz);
    if (o.wallsOnly)
        o.state = 0;
    if (o.state > 1.5)
        s.emission += o.colour * (edges * _ArenaFloorLine.z + (1.0 - edges) * o.fill * _ArenaFloorLine.w);
    else if (o.state > 0.5)
        s.albedo = lerp(s.albedo, o.colour, edges);
    else
        s.emission += _ArenaOutsideLine.rgb * (edges * _ArenaFloorLine.y);
#elif defined(_ARENA_WALL) || defined(_ARENA_BLOCK)
    // Side faces: u along the face and level, v up, both in metres, so yawed and stretched pieces keep straight, even stripes.
    float2 along = float2(normalWS.z, -normalWS.x);
    along *= rsqrt(max(dot(along, along), 1e-6));
    float circuit = ArenaCircuit(float2(dot(positionWS.xz, along), positionWS.y), aa);
    float edgeMetres = ArenaFaceEdgeMetres(uv, positionWS); // screen derivatives: taken outside any branch
    float top = ArenaLineMask(edgeMetres, 2.0 * _ArenaStripe.w, aa); // a band from the edge inward
    float stripe = normalWS.y > 0.5 ? top : (normalWS.y < -0.5 ? 0 : circuit);
    float panelEdge = ArenaLineMask(edgeMetres, 2.0 * _ArenaPanelEdge.w, aa); // 0 at width 0
    stripe *= 1.0 - panelEdge;
    s.emission += _ArenaPanelEdge.rgb * panelEdge;
    ArenaOwner o = ArenaOwnerAt(positionWS.xz);
    if (o.state > 1.5)
        s.emission += o.colour * (stripe * _ArenaStripeGlow.y);
    else if (o.state > 0.5)
        s.albedo = lerp(s.albedo, o.colour, stripe);
    else
        s.emission += _ArenaOutsideLine.rgb * (stripe * _ArenaStripeGlow.x);
#endif
}

#endif
