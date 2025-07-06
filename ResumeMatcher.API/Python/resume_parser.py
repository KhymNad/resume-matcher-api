from flask import Flask, request, jsonify
import pdfplumber
import re

app = Flask(__name__)

def clean_text(text):
    # Add spacing for camelCase and normalize whitespace
    text = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", text)
    text = re.sub(r"([.,:;!?])(?=\S)", r"\1 ", text)
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
