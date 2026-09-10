#!/usr/bin/env python3
"""Build the two-page Italian supply contract `live-smoke.sh` uploads.

A real PDF, not a fixture shape: object offsets are recorded and written into a cross-reference
table with a `trailer`/`startxref`, because Azure AI Document Intelligence parses the file for
real and refuses one whose xref does not resolve. Content is Latin-1/ASCII only so a byte offset
equals a character offset while the file is being assembled.

The text carries the facts `live-smoke.sh` then asserts on: supplier legal name, EUR currency,
annual fee 48000, ISO-formatted dates, auto-renewal, payment terms, governing law, and a clause
worth citing on page 2.

Usage: python make-smoke-pdf.py [output.pdf]
"""
from __future__ import annotations

import sys

PAGE_ONE = [
    "CONTRATTO QUADRO DI FORNITURA",
    "",
    "tra Rossi Software S.r.l., con sede in Via Torino 12, 20123 Milano (il Fornitore)",
    "e Raffa Demo S.p.A., con sede in Via Roma 8, 00184 Roma (il Cliente).",
    "",
    "1. Oggetto. Il Fornitore concede in licenza la piattaforma Rossi Cloud Suite e",
    "   presta i servizi di supporto descritti nell'Allegato A.",
    "",
    "2. Decorrenza e durata. Il presente Contratto ha efficacia dal 1 gennaio 2026 e",
    "   dura 36 mesi, con scadenza il 31 dicembre 2028.",
    "",
    "3. Corrispettivi. Il canone annuo e' pari a EUR 48.000,00 (quarantottomila euro),",
    "   IVA esclusa, fatturato annualmente in via anticipata.",
    "",
    "4. Termini di pagamento. Le fatture sono pagabili a 30 giorni data fattura.",
]

PAGE_TWO = [
    "5. Rinnovo automatico. Il Contratto si rinnova automaticamente per periodi",
    "   successivi di 12 mesi, salvo disdetta comunicata da una delle Parti con",
    "   preavviso di 90 giorni rispetto alla scadenza.",
    "",
    "6. Limitazione di responsabilita'. La responsabilita' complessiva del Fornitore",
    "   e' limitata al corrispettivo pagato dal Cliente nei 12 mesi precedenti",
    "   l'evento dannoso, salvo dolo o colpa grave.",
    "",
    "7. Protezione dei dati. Il Fornitore tratta i dati personali del Cliente in",
    "   qualita' di responsabile del trattamento ai sensi del Regolamento (UE) 2016/679.",
    "",
    "8. Legge applicabile e foro. Il Contratto e' regolato dalla legge italiana. Per",
    "   ogni controversia e' competente in via esclusiva il Foro di Milano.",
    "",
    "Milano, 15 dicembre 2025",
    "Rossi Software S.r.l.                    Raffa Demo S.p.A.",
]


def escape(text: str) -> str:
    return text.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")


def content_stream(lines: list[str]) -> str:
    body = ["BT", "/F1 11 Tf", "14 TL", "56 760 Td"]
    for line in lines:
        body.append(f"({escape(line)}) Tj T*")
    body.append("ET")
    return "\n".join(body)


def build() -> bytes:
    streams = [content_stream(PAGE_ONE), content_stream(PAGE_TWO)]

    objects = [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
        "/Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
        "/Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        f"<< /Length {len(streams[0])} >>\nstream\n{streams[0]}\nendstream",
        f"<< /Length {len(streams[1])} >>\nstream\n{streams[1]}\nendstream",
    ]

    out = "%PDF-1.7\n"
    offsets: list[int] = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n{body}\nendobj\n"

    xref_offset = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n"
    for offset in offsets:
        out += f"{offset:010d} 00000 n \n"
    out += (
        f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
        f"startxref\n{xref_offset}\n%%EOF\n"
    )

    assert out.isascii(), "the PDF body must stay ASCII so byte offsets match character offsets"
    return out.encode("latin-1")


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "contratto-quadro-smoke.pdf"
    data = build()
    with open(target, "wb") as handle:
        handle.write(data)
    print(f"{target}: {len(data)} bytes, 2 pages")
