# Inlines each direction's src/ parts and the shared data into one self-contained
# index.html per direction, so a mockup opens from a single file with nothing external.
import pathlib
import re
import sys

here = pathlib.Path(__file__).resolve().parent.parent
include = re.compile(r"/\*@include ([^*]+?)\*/")


def build(direction: pathlib.Path) -> pathlib.Path:
    source = direction / "src" / "index.html"
    text = source.read_text(encoding="utf-8")

    def expand(match: re.Match) -> str:
        target = (direction / "src" / match.group(1).strip()).resolve()
        return target.read_text(encoding="utf-8")

    text = include.sub(expand, text)
    out = direction / "index.html"
    out.write_text(text, encoding="utf-8", newline="\n")
    return out


names = sys.argv[1:] or [p.name for p in sorted(here.iterdir()) if (p / "src" / "index.html").exists()]

for name in names:
    print(build(here / name))
