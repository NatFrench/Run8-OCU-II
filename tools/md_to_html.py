"""
Converts a Markdown document (the operator's manual) into a single standalone HTML page that opens
in any browser. Heading anchors match GitHub's, so the manual's contents links keep working, and
local images are embedded so the page needs no other files.

Usage: python tools/md_to_html.py <input.md> <output.html>
Requires: pip install markdown
"""
import base64
import html
import mimetypes
import os
import re
import sys

import markdown

STYLE = """
body { font-family: 'Segoe UI', Arial, sans-serif; max-width: 920px; margin: 2em auto; padding: 0 1.2em;
       line-height: 1.5; color: #222; }
h1, h2, h3 { line-height: 1.25; }
h2 { border-bottom: 1px solid #ccc; padding-bottom: .2em; margin-top: 2em; }
table { border-collapse: collapse; margin: 1em 0; }
th, td { border: 1px solid #ccc; padding: .35em .6em; vertical-align: top; text-align: left; }
th { background: #f2f2f2; }
code { background: #f2f2f2; padding: .1em .3em; border-radius: 3px; font-size: 92%; }
pre { background: #f6f6f6; padding: .8em; overflow-x: auto; line-height: 1.25; }
pre code { background: none; padding: 0; }
hr { border: none; border-top: 1px solid #ddd; margin: 2em 0; }
img { max-width: 100%; height: auto; }
blockquote { margin: 1em 0; padding: .3em 1em; border-left: 4px solid #d0a020; background: #fdf8ea; }
@media print { h2, h3 { break-after: avoid; } tr, img { break-inside: avoid; } }
"""


def slugify(value, separator):
    value = re.sub(r"[^\w\s-]", "", value.strip().lower())
    return re.sub(r"\s", separator, value)


def embed_images(body, folder):
    def data_uri(match):
        src = match.group(2)
        if re.match(r"[a-z]+:", src):
            return match.group(0)
        path = os.path.join(folder, src)
        mime = mimetypes.guess_type(path)[0] or "application/octet-stream"
        with open(path, "rb") as f:
            data = base64.b64encode(f.read()).decode("ascii")
        return f"{match.group(1)}data:{mime};base64,{data}\""
    return re.sub(r'(<img[^>]*?src=")([^"]+)"', data_uri, body)


def main():
    source, target = sys.argv[1], sys.argv[2]
    with open(source, encoding="utf-8") as f:
        text = f.read()
    title = next((line[2:].strip() for line in text.splitlines() if line.startswith("# ")), "Manual")
    body = markdown.markdown(text, extensions=["tables", "fenced_code", "toc"],
                             extension_configs={"toc": {"slugify": slugify}})
    body = embed_images(body, os.path.dirname(os.path.abspath(source)))
    page = (f"<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
            f"<title>{html.escape(title)}</title>\n<style>{STYLE}</style>\n</head>\n<body>\n{body}\n</body>\n</html>\n")
    with open(target, "w", encoding="utf-8") as f:
        f.write(page)


if __name__ == "__main__":
    main()
