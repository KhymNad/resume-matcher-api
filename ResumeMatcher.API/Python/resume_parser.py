from flask import Flask, request, jsonify
import pdfplumber
import re

app = Flask(__name__)

def clean_text(text):
    # Merge hyphenated words across line breaks
    text = re.sub(r"(\w+)-\n(\w+)", r"\1\2", text)

    # Remove non-ASCII or control characters
    text = re.sub(r"[^\x00-\x7F]+", " ", text)

    # Replace common bullet symbols
    text = re.sub(r"[•●◦◆▶➤►★▪]", " ", text)

    # Convert fancy quotes/dashes to ASCII
    text = text.replace("“", '"').replace("”", '"').replace("‘", "'").replace("’", "'")
    text = text.replace("–", "-").replace("—", "-")

    # Normalize bullets at line starts
    text = re.sub(r"^[\s\-•●◦*]+", "- ", text, flags=re.MULTILINE)

    # Add spacing for camelCase and punctuation
    text = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", text)
    text = re.sub(r"([.,:;!?])(?=\S)", r"\1 ", text)

    # Remove extra newlines and repeated characters
    text = re.sub(r"[-=]{2,}", " ", text)
    text = re.sub(r"\n{2,}", "\n", text)

    # Normalize whitespace
    text = re.sub(r"\s+", " ", text)

    return text.strip()

@app.route("/extract-resume", methods=["POST"])
def extract_resume():
    file = request.files["file"]
    with pdfplumber.open(file) as pdf:
        text = "\n".join(page.extract_text() or "" for page in pdf.pages)

    cleaned_text = clean_text(text)
    return jsonify({"cleaned_text": cleaned_text})

if __name__ == "__main__":
    app.run(port=5001)
