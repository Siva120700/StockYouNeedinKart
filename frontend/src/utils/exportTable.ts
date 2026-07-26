import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";
import * as XLSX from "xlsx";

export type ExportColumn<T> = {
  header: string;
  value: (row: T) => string | number | null | undefined;
};

function cellText(value: string | number | null | undefined): string {
  if (value == null) return "";
  if (typeof value === "number") {
    return Number.isFinite(value) ? String(value) : "";
  }
  return String(value);
}

function buildMatrix<T>(columns: ExportColumn<T>[], rows: T[]): string[][] {
  const headers = columns.map((c) => c.header);
  const body = rows.map((row) => columns.map((c) => cellText(c.value(row))));
  return [headers, ...body];
}

function stampFileName(base: string, ext: string): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  const stamp = `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}-${pad(d.getHours())}${pad(d.getMinutes())}`;
  return `${base}-${stamp}.${ext}`;
}

export function downloadPdfTable<T>(opts: {
  title: string;
  fileName: string;
  columns: ExportColumn<T>[];
  rows: T[];
}): void {
  const { title, fileName, columns, rows } = opts;
  const doc = new jsPDF({ orientation: "landscape", unit: "pt", format: "a4" });
  const when = new Date().toLocaleString("en-IN");

  doc.setFontSize(14);
  doc.text(title, 40, 36);
  doc.setFontSize(9);
  doc.setTextColor(100);
  doc.text(`${when}  ·  ${rows.length} row${rows.length === 1 ? "" : "s"}`, 40, 52);
  doc.setTextColor(0);

  const head = [columns.map((c) => c.header)];
  const body = rows.map((row) => columns.map((c) => cellText(c.value(row))));

  autoTable(doc, {
    head,
    body,
    startY: 62,
    styles: { fontSize: 7, cellPadding: 3, overflow: "linebreak" },
    headStyles: { fillColor: [33, 33, 33], textColor: 255, fontStyle: "bold" },
    alternateRowStyles: { fillColor: [245, 245, 245] },
    margin: { left: 28, right: 28 },
  });

  doc.save(fileName.endsWith(".pdf") ? fileName : stampFileName(fileName, "pdf"));
}

export function downloadExcelTable<T>(opts: {
  sheetName: string;
  fileName: string;
  columns: ExportColumn<T>[];
  rows: T[];
}): void {
  const { sheetName, fileName, columns, rows } = opts;
  const matrix = buildMatrix(columns, rows);
  const ws = XLSX.utils.aoa_to_sheet(matrix);
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, sheetName.slice(0, 31));
  XLSX.writeFile(
    wb,
    fileName.endsWith(".xlsx") ? fileName : stampFileName(fileName, "xlsx"),
  );
}

export function exportStamp(base: string, ext: "pdf" | "xlsx"): string {
  return stampFileName(base, ext);
}
