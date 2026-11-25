"""Prints a Markdown table of test counts from every .trx file under TestResults/ (used by CI)."""
import glob
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

print("| Total | Passed | Failed | Not run |")
print("|---|---|---|---|")
for path in glob.glob("TestResults/**/*.trx", recursive=True):
    counters = ET.parse(path).getroot().find("t:ResultSummary/t:Counters", NS).attrib
    not_run = int(counters["total"]) - int(counters["executed"])
    print(f"| {counters['total']} | {counters['passed']} | {counters['failed']} | {not_run} |")
print()
