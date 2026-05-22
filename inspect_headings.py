import fitz

pdf_path = r"d:\ProjectGameCard\GameCardClient\ตัวอย่างเอกสาร\ตัวอย่างเอกสารของเพื่อน\การพัฒนาเว็บไซต์อีคอมเมิร์ซ Supply Go.pdf"
doc = fitz.open(pdf_path)

with open("pages_48_60.txt", "w", encoding="utf-8") as f:
    for i in range(45, len(doc)):
        f.write(f"\n\n=== PAGE {i+1} ===\n\n")
        f.write(doc[i].get_text())

print("Successfully written pages 46 to 60 to pages_48_60.txt")
