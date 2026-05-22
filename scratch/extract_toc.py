import fitz

pdf_path = r"d:\ProjectGameCard\GameCardClient\ตัวอย่างเอกสาร\ตัวอย่างเอกสารของเพื่อน\การพัฒนาเว็บไซต์อีคอมเมิร์ซ Supply Go.pdf"
doc = fitz.open(pdf_path)

with open(r"d:\ProjectGameCard\GameCardClient\scratch\toc_output.txt", "w", encoding="utf-8") as f:
    f.write(f"Total pages: {len(doc)}\n")
    
    f.write("\n=== SCANNING FIRST 15 PAGES ===\n")
    for i in range(15):
        text = doc[i].get_text()
        if text.strip():
            f.write(f"\n--- PAGE {i+1} (Length: {len(text)}) ---\n")
            f.write(text)
            
    f.write("\n=== SEARCHING FOR CHAPTERS ===\n")
    for i in range(len(doc)):
        text = doc[i].get_text()
        if "บทที่" in text:
            lines = text.split('\n')
            for line in lines:
                if "บทที่" in line:
                    f.write(f"Page {i+1} contains: {line}\n")
