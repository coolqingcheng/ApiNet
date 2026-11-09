"""Prepare the OFL font for searchable Skia PDFs (requires fonttools).

Noto CJK shares glyph IDs between some ideographs and radical characters.
Skia's PDF writer reverses that cmap, which can copy a radical instead of
the original ideograph. Give each Unicode alias its own identical glyph.
"""
from collections import defaultdict
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parent.parent
source = root / "src/ApiNet.Android/Assets/Fonts/NotoSansCJKsc-Regular.otf"
destination = root / "src/ApiNet.UI/Assets/Fonts/ApiNetDocumentSC-Regular.otf"
font = TTFont(source, recalcTimestamp=False)
options = subset.Options()
options.layout_features = ["*"]
options.name_IDs = ["*"]
options.name_legacy = True
options.name_languages = ["*"]
subsetter = subset.Subsetter(options=options)
subsetter.populate(unicodes=font.getBestCmap().keys())
subsetter.subset(font)

aliases = defaultdict(list)
for codepoint, glyph in font.getBestCmap().items():
    aliases[glyph].append(codepoint)
order = list(font.getGlyphOrder())
metrics = font["hmtx"].metrics
vertical_metrics = font["vmtx"].metrics if "vmtx" in font else None
top = font["CFF "].cff.topDictIndex[0]
charstrings = top.CharStrings
used_glyphs = set(order)
free_cids = iter(f"cid{n:05}" for n in range(1, 65536) if f"cid{n:05}" not in used_glyphs)
replacements = {}
for original, codepoints in aliases.items():
    for codepoint in codepoints[1:]:
        duplicate = next(free_cids)
        glyph, selector = charstrings.getItemAndSelector(original)
        charstrings.charStrings[duplicate] = len(charstrings.charStringsIndex)
        charstrings.charStringsIndex.append(glyph)
        if hasattr(top, "FDSelect"):
            top.FDSelect.append(selector)
        top.charset.append(duplicate)
        metrics[duplicate] = metrics[original]
        if vertical_metrics is not None:
            vertical_metrics[duplicate] = vertical_metrics[original]
        if "VORG" in font and original in font["VORG"].VOriginRecords:
            font["VORG"].VOriginRecords[duplicate] = font["VORG"].VOriginRecords[original]
        order.append(duplicate)
        replacements[codepoint] = duplicate
assert len(order) < 65536
font.setGlyphOrder(order)
for table in font["cmap"].tables:
    if table.isUnicode():
        for codepoint in table.cmap.keys() & replacements.keys():
            table.cmap[codepoint] = replacements[codepoint]

for record in font["name"].names:
    names = {1: "ApiNet Document SC", 2: "Regular", 3: "ApiNetDocumentSC-Regular-1", 4: "ApiNet Document SC Regular", 6: "ApiNetDocumentSC-Regular", 16: "ApiNet Document SC", 17: "Regular"}
    if record.nameID in names:
        record.string = names[record.nameID].encode(record.getEncoding())
font["CFF "].cff.fontNames = ["ApiNetDocumentSC-Regular"]
top.FamilyName = "ApiNet Document SC"
top.FullName = "ApiNet Document SC Regular"
font.save(destination)
check = TTFont(destination).getBestCmap()
assert len(check.values()) == len(set(check.values()))
assert set(check) == set(TTFont(source).getBestCmap())
print(f"Prepared {len(check)} Unicode characters with distinct glyph IDs: {destination}")
