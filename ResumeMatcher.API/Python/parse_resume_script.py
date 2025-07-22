# parse_resume.py
import sys
import pdfplumber
import re

def clean_text(text):
    text = re.sub(r"(\w+)-\n(\w+)", r"\1\2", text)
    text = re.sub(r"[^\x00-\x7F]+", " ", text)
    text = re.sub(r"[•●◦◆▶➤►★▪]", " ", text)
    text = text.replace("“", '"').replace("”", '"').replace("‘", "'").replace("’", "'")
    text = text.replace("–", "-").replace("—", "-")
    text = re.sub(r"^[\s\-•●◦*]+", "- ", text, flags=re.MULTILINE)
    text = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", text)
    text = re.sub(r"([.,:;!?])(?=\S)", r"\1 ", text)
    text = re.sub(r"[-=]{2,}", " ", text)
    text = re.sub(r"\n{2,}", "\n", text)
    text = re.sub(r"\s+", " ", text)
    return text.strip()

def extract_resume(file_path):
    with pdfplumber.open(file_path) as pdf:
        text = "\n".join(page.extract_text() or "" for page in pdf.pages)
    return clean_text(text)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python parse_resume.py <file_path>", file=sys.stderr)
        sys.exit(1)

    file_path = sys.argv[1]
    try:
        result = extract_resume(file_path)
        print(result)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
