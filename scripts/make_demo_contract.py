"""Generates web/public/samples/Demo_SaaS_MSA_2024.pdf: the fictitious demo contract that the
"Load the demo contract" button on Today uploads through the real upload path.
Run: python3 scripts/make_demo_contract.py (needs reportlab)."""
from pathlib import Path
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table

OUT = Path(__file__).resolve().parents[1] / "web" / "public" / "samples" / "Demo_SaaS_MSA_2024.pdf"
s = getSampleStyleSheet()
H, B = s["Heading2"], s["BodyText"]

sections = [
    ("1. Parties", "This Master Subscription Agreement (the \"Agreement\") is entered into on 1 October 2024 "
     "(the \"Effective Date\") between Nimbus Cloud Software Ltd., a fictitious supplier used for demonstration "
     "purposes (\"Supplier\"), and Demo Customer S.p.A. (\"Customer\")."),
    ("2. Services", "Supplier shall provide Customer with access to the Nimbus CRM Enterprise platform, including "
     "Sales Cloud and Service Cloud modules, for 250 named users, together with Premier Support."),
    ("3. Subscription Term", "The Initial Subscription Term begins on 1 November 2024 and ends on 31 October 2027 "
     "(thirty-six (36) months)."),
    ("4. Fees", "Customer shall pay annual subscription fees of EUR 486,000.00 (four hundred eighty-six thousand euro), "
     "invoiced annually in advance. Payment terms: sixty (60) days from the date of invoice."),
    ("5. Price Adjustment", "Upon each renewal, Supplier may increase the fees by the greater of seven percent (7%) or "
     "the CPI; any such increase shall be notified no later than sixty (60) days before renewal."),
    ("6. Renewal", "Unless terminated, this Agreement shall renew automatically for successive periods of twelve (12) "
     "months, unless either party gives written notice of non-renewal at least ninety (90) days before the end of "
     "the then-current term."),
    ("7. Termination", "Either party may terminate this Agreement for material breach not cured within thirty (30) "
     "days of written notice. Notices shall be sent by certified email (PEC) to the addresses in Schedule 1."),
    ("8. Limitation of Liability", "In no event shall either party's aggregate liability exceed the fees paid or "
     "payable in the twelve (12) months preceding the claim, except for breach of confidentiality obligations or "
     "infringement of intellectual property rights."),
    ("9. Data Protection", "Supplier shall process personal data as a processor under Regulation (EU) 2016/679 and the "
     "Data Processing Addendum attached as Schedule 2. Data is hosted in the European Union."),
    ("10. Governing Law", "This Agreement is governed by the laws of Italy. The courts of Milan have exclusive jurisdiction."),
]

story = [Paragraph("MASTER SUBSCRIPTION AGREEMENT", s["Title"]),
         Paragraph("DEMO DOCUMENT — fictitious parties, for product demonstration only.", B), Spacer(1, 12)]
for h, body in sections:
    story += [Paragraph(h, H), Paragraph(body, B), Spacer(1, 6)]
story += [Spacer(1, 24), Table([["For Supplier", "For Customer"],
                                ["Nimbus Cloud Software Ltd.", "Demo Customer S.p.A."],
                                ["Date: 1 October 2024", "Date: 1 October 2024"]], colWidths=[240, 240])]
SimpleDocTemplate(str(OUT), pagesize=A4, title="Demo SaaS MSA 2024").build(story)
print(OUT)
