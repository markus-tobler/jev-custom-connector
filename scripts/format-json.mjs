// Re-indents JSON files with two spaces so exported connector files produce readable diffs.
import { readFileSync, writeFileSync } from "node:fs";

for (const file of process.argv.slice(2)) {
  const text = readFileSync(file, "utf8").replace(/^\uFEFF/, "");
  const formatted = JSON.stringify(JSON.parse(text), null, 2) + "\n";
  if (formatted !== text) {
    writeFileSync(file, formatted, "utf8");
  }
}
