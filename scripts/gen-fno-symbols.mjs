import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const jsonPath = path.join(root, "database", "fno_underlyings.json");
const outPath = path.join(
  root,
  "backend",
  "src",
  "StockYouNeed.Application",
  "Services",
  "FnoUnderlyingSymbols.cs"
);

const symbols = JSON.parse(fs.readFileSync(jsonPath, "utf8"));
const lines = symbols.map((s) => `        "${s}",`).join("\n");
const content = `namespace StockYouNeed.Application.Services;

/// <summary>NSE F&amp;O equity underlyings (from Angel FUTSTK, ${symbols.length} symbols).</summary>
internal static class FnoUnderlyingSymbols
{
    internal static readonly string[] All =
    [
${lines}
    ];
}
`;

fs.writeFileSync(outPath, content);
console.log(`Wrote ${symbols.length} symbols to ${outPath}`);
