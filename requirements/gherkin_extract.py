import io
import json
import sys
from pathlib import Path

from docutils import nodes
from sphinx.application import Sphinx
from sphinx.directives.code import CodeBlock
from sphinx_needs.data import SphinxNeedsData
from sphinx_needs.nodes import Need


class LocatedCodeBlock(CodeBlock):
    def run(self) -> list[nodes.Node]:
        result = super().run()
        for root in result:
            for literal in root.findall(nodes.literal_block, include_self=True):
                literal["bdd_selected"] = self.arguments == ["gherkin"]
                if not literal["bdd_selected"]:
                    continue
                source_lines = {source: Path(source).read_text(encoding="utf-8").splitlines()
                                for source in {source for source, _ in self.content.items}}
                locations = [
                    {"line": offset + 1,
                     "column": len(source_lines[source][offset].expandtabs(8).rstrip()) - len(content.rstrip()) + 1}
                    for content, (source, offset) in zip(self.content, self.content.items)
                ]
                locations.append({"line": locations[-1]["line"] + 1 if locations else literal.line + 2, "column": 1})
                literal["bdd_lines"] = locations
        return result


def main() -> None:
    repository = Path(sys.argv[1]).resolve()
    output = Path(sys.argv[2]).resolve()
    selected = {str((repository / path).resolve()) for path in sys.argv[4:]}
    blocks = []
    status = io.StringIO()
    warnings = io.StringIO()

    def collect(app: Sphinx, doctree: nodes.document) -> None:
        if str(Path(app.env.doc2path(app.env.docname)).resolve()) not in selected:
            return
        needs = SphinxNeedsData(app.env).get_needs_mutable()
        for literal in doctree.findall(nodes.literal_block):
            if not literal.get("bdd_selected", False):
                continue
            parent = literal.parent
            while parent is not None and not isinstance(parent, Need):
                parent = parent.parent
            if parent is None or needs[parent["refid"]]["type"] != "design":
                raise ValueError(f"{literal.source}:{literal.line}: gherkin 代码块必须位于 design 节点")
            locations = literal["bdd_lines"]
            blocks.append({
                "fileRelativePath": Path(literal.source).resolve().relative_to(repository).as_posix(),
                "needId": parent["refid"],
                "blockIndex": len(blocks),
                "startLine": literal.line,
                "startColumn": 1,
                "endLine": locations[-2]["line"] if len(locations) > 1 else literal.line,
                "endColumn": locations[-2]["column"] + len(literal.astext().splitlines()[-1]) if len(locations) > 1 else 1,
                "lines": locations,
                "source": literal.astext(),
            })

    app = Sphinx(str(repository / "requirements"), str(repository / "requirements"),
                 str(output / "output"), str(output / "doctrees"), "dummy",
                 status=status, warning=warnings, freshenv=True, warningiserror=True,
                 confoverrides={"toolkit_sources_file": sys.argv[3]})
    app.add_directive("code-block", LocatedCodeBlock, override=True)
    app.connect("doctree-read", collect)
    app.build(filenames=[str(repository / path) for path in sys.argv[4:]])
    if app.statuscode:
        raise ValueError(warnings.getvalue())
    print(json.dumps({"blocks": blocks}, ensure_ascii=False))


if __name__ == "__main__":
    main()
