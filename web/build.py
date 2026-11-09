#!/usr/bin/env python3
"""Writes the VKonvert website's HTML pages, sitemap.xml and robots.txt into web/public.

Every tool page is a real page with its own title, description, how-to and FAQ (for Google),
and the same "machine" that does the work in the browser.  Run:  python3 web/build.py
"""
import html
import json
import os
from datetime import date

SITE = os.environ.get("VK_SITE", "https://vkonvert.vercel.app")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "public")
GITHUB = "https://github.com/vishnuvk2k/VKonvert"
SETUP = GITHUB + "/raw/main/download/VKonvert-Setup-2.0.exe"
PORTABLE = GITHUB + "/raw/main/download/VKonvert-2.0-Portable.zip"
VERSION = "2.0"

MARK = ('<svg viewBox="0 0 64 64" aria-hidden="true"><rect width="64" height="64" rx="15" fill="#D9342B"/>'
        '<g fill="#fff"><rect x="13.25" y="13.25" width="7.5" height="7.5"/><rect x="17.95" y="22.25" width="7.5" height="7.5"/>'
        '<rect x="22.55" y="31.25" width="7.5" height="7.5"/></g><path d="M28.4 39.2 L32 46 L48.4 14.6" fill="none" stroke="#fff" stroke-width="7.5"/></svg>')

# slug, tool, target, short name, tile example, tile text, <title>, meta description, h1, lead, steps, faqs, related
PAGES = [
    dict(slug="convert-image", tool="convert", target="JPG", name="Convert image", ex="HEIC→JPG",
         tile="Between JPG, PNG, WebP, ICO, BMP and PDF, including iPhone photos.",
         title="Convert Images Online Free: JPG, PNG, WebP, HEIC | VKonvert",
         desc="Convert images between JPG, PNG, WebP, ICO, BMP and PDF in your browser. Opens iPhone HEIC photos. Free, no upload, no sign-up.",
         h1="Convert images to JPG, PNG, WebP and more",
         lead="Drop your pictures, pick a format, done. Opens HEIC, AVIF, TIFF, GIF and more. Your files never leave your device.",
         steps=[("Add pictures", "Drop them on the page or tap Choose pictures. You can add a whole batch."),
                ("Pick a format", "Tap JPG, PNG, WebP, PDF, ICO or BMP. For JPG and WebP, set the quality."),
                ("Save", "Save each picture, or all of them as one ZIP.")],
         faqs=[("Which formats can it open?", "JPG, PNG, WebP, HEIC and HEIF (iPhone photos), AVIF, GIF, BMP, TIFF and ICO."),
               ("Can it save as HEIC or AVIF?", "Not in the browser, because browsers can’t create those files. The free VKonvert app for Windows can."),
               ("What happens to see-through parts when I save as JPG?", "JPG can’t be see-through, so those parts become white, or black if you choose."),
               ("Are my pictures uploaded?", "No. Everything happens inside your browser, on your device.")],
         related=["heic-to-jpg", "compress-image", "resize-image", "jpg-to-pdf"]),
    dict(slug="heic-to-jpg", tool="convert", target="JPG", name="HEIC to JPG", ex="HEIC→JPG", tile="Turn iPhone photos into JPGs that open everywhere.",
         title="HEIC to JPG Converter, Free and Private (No Upload) | VKonvert",
         desc="Convert iPhone HEIC photos to JPG in your browser. Batch convert, keep quality, no upload and no sign-up.",
         h1="HEIC to JPG",
         lead="iPhones save photos as HEIC, which many websites and Windows apps can’t open. Turn them into JPGs here. Nothing is uploaded.",
         steps=[("Add your HEIC photos", "Drop them here or tap Choose pictures. Dozens at once is fine."),
                ("Keep JPG selected", "Adjust the quality if you like; 85 looks the same as the original."),
                ("Save", "Save each JPG, or all of them as a ZIP.")],
         faqs=[("Why do my iPhone photos end in .HEIC?", "HEIC is Apple’s space-saving photo format. It’s great on iPhone, but many sites and apps only accept JPG."),
               ("Does converting lose quality?", "At quality 85 or higher the difference isn’t visible. The photo’s rotation is kept."),
               ("Is it safe for private photos?", "Yes. The photos are converted on your own device and never sent anywhere.")],
         related=["convert-image", "compress-image", "jpg-to-pdf", "remove-photo-info"]),
    dict(slug="png-to-jpg", tool="convert", target="JPG", name="PNG to JPG", ex="PNG→JPG", tile="Smaller files from screenshots and graphics.",
         title="PNG to JPG Converter, Free and Private | VKonvert",
         desc="Convert PNG to JPG in your browser. Choose the quality and the background for see-through parts. No upload.",
         h1="PNG to JPG", lead="Turn PNGs into smaller JPGs. Pick white or black for see-through parts. Nothing is uploaded.",
         steps=[("Add PNGs", "Drop them or tap Choose pictures."), ("Pick quality and background", "85 is a good balance; see-through parts become white unless you choose black."), ("Save", "Save each JPG or all as a ZIP.")],
         faqs=[("Why is my JPG smaller than the PNG?", "JPG compresses photos much better. For logos with sharp edges, PNG often looks cleaner."),
               ("Is it free?", "Yes. No sign-up, no watermark, no limits.")],
         related=["jpg-to-png", "compress-image", "convert-image", "resize-image"]),
    dict(slug="jpg-to-png", tool="convert", target="PNG", name="JPG to PNG", ex="JPG→PNG", tile="Lossless PNG copies of your photos.",
         title="JPG to PNG Converter, Free and Private | VKonvert",
         desc="Convert JPG to PNG in your browser. No quality loss, no upload, no sign-up.",
         h1="JPG to PNG", lead="Save photos as PNG without further quality loss. Nothing is uploaded.",
         steps=[("Add JPGs", "Drop them or tap Choose pictures."), ("Keep PNG selected", "PNG has no quality setting; it keeps every pixel."), ("Save", "Save each PNG or all as a ZIP.")],
         faqs=[("Will the PNG be bigger?", "Usually yes. PNG keeps every pixel, so photos take more space than as JPG."),
               ("Does it make the background see-through?", "No. It keeps the picture as it is.")],
         related=["png-to-jpg", "convert-image", "compress-image", "resize-image"]),
    dict(slug="webp-to-jpg", tool="convert", target="JPG", name="WebP to JPG", ex="WebP→JPG", tile="Open web images in any app.",
         title="WebP to JPG Converter, Free and Private | VKonvert",
         desc="Convert WebP images to JPG in your browser so they open in any app. Batch convert, no upload.",
         h1="WebP to JPG", lead="Saved a picture from a website and it’s a .webp? Turn it into a JPG that opens everywhere.",
         steps=[("Add WebP files", "Drop them or tap Choose pictures."), ("Keep JPG selected", "Or pick PNG to keep see-through parts."), ("Save", "Save each file or all as a ZIP.")],
         faqs=[("What is WebP?", "A modern image format used by many websites. Some older apps can’t open it."),
               ("Can I convert to PNG instead?", "Yes. Tap PNG to keep see-through parts.")],
         related=["jpg-to-webp", "convert-image", "png-to-jpg", "compress-image"]),
    dict(slug="jpg-to-webp", tool="convert", target="WebP", name="JPG to WebP", ex="JPG→WebP", tile="Lighter images for websites.",
         title="JPG to WebP Converter, Free and Private | VKonvert",
         desc="Convert JPG and PNG to WebP in your browser for faster websites. Choose the quality. No upload.",
         h1="JPG to WebP", lead="WebP pictures are often a third smaller than JPGs at the same quality, which makes websites faster.",
         steps=[("Add pictures", "JPG or PNG; drop them or tap Choose pictures."), ("Set the quality", "80–85 is a good choice for websites."), ("Save", "Save each WebP or all as a ZIP.")],
         faqs=[("Does WebP keep see-through parts?", "Yes, unlike JPG."), ("Which browsers can save WebP?", "Chrome, Edge, Firefox and other modern browsers. Older Safari can’t; use the Windows app there.")],
         related=["webp-to-jpg", "compress-image", "resize-image", "convert-image"]),
    dict(slug="compress-image", tool="compress", name="Compress image", ex="5 MB→480 KB", tile="Pick a size limit like 100 KB, or a quality.",
         title="Compress Image to 100 KB, 200 KB or 500 KB, Free | VKonvert",
         desc="Compress JPG, PNG and WebP to a size you choose, like under 100 KB or 500 KB. Free, in your browser, no upload.",
         h1="Compress JPG, PNG and WebP",
         lead="Make photos small enough for forms, email and websites. Choose a size limit or a quality. Nothing is uploaded.",
         steps=[("Add pictures", "Drop them or tap Choose pictures."), ("Choose a size", "Pick 50 KB to 1 MB, type your own, or switch to Quality."), ("Save", "Save each picture or all as a ZIP.")],
         faqs=[("How do I make a photo under 100 KB?", "Tap Size limit, then 100 KB. VKonvert finds the best quality that fits, and makes the picture a little smaller in size only if it has to."),
               ("Will it still look good?", "Most photos look the same down to quality 70–75. Very small limits make photos softer."),
               ("What if my file is already small?", "VKonvert never gives you a bigger file. If it can’t shrink it, you get the original back."),
               ("Are photos uploaded?", "No. Everything happens on your device.")],
         related=["resize-image", "heic-to-jpg", "compress-pdf", "remove-photo-info"]),
    dict(slug="resize-image", tool="resize", name="Resize image", ex="4000→1080", tile="By pixels, percent, square post or passport photo.",
         title="Resize Image Online Free: Pixels, Percent, Passport | VKonvert",
         desc="Resize photos to 1080 px, 1920 px, 50%, a square post or a passport photo (35×45 mm). Free, in your browser, no upload.",
         h1="Resize images",
         lead="Make pictures smaller for posts, forms and email, or crop them to a square or passport size.",
         steps=[("Add pictures", "Drop them or tap Choose pictures."), ("Pick a size", "1080 px for Instagram, 1920 px for HD, 50%, Square or Passport, or type your own."), ("Save", "Save each picture or all as a ZIP.")],
         faqs=[("What size is a passport photo?", "VKonvert makes 35 × 45 mm at 300 dpi (413 × 531 pixels), cropped from the centre. Check your country’s rules for the face size."),
               ("Will small pictures be stretched?", "No. Small pictures stay as they are unless you turn that off.")],
         related=["compress-image", "convert-image", "jpg-to-pdf", "remove-photo-info"]),
    dict(slug="remove-photo-info", tool="removeinfo", name="Remove photo info", ex="GPS ✕", tile="Delete location and camera details before sharing.",
         title="Remove Photo Location and EXIF Data, Free | VKonvert",
         desc="Remove GPS location, camera model, date and other hidden EXIF data from photos before you share them. In your browser, no upload.",
         h1="Remove location and camera info from photos",
         lead="Photos can carry the exact place they were taken. Remove it, and other hidden details, before you share.",
         steps=[("Add photos", "Drop them or tap Choose pictures."), ("Tap Remove info", "VKonvert saves clean copies."), ("Share the clean copies", "Your originals stay as they were.")],
         faqs=[("What is removed?", "Location (GPS), camera and phone model, date taken, editing software and hidden thumbnails."),
               ("Does the picture change?", "No. It looks the same. It’s saved again at high quality without the hidden details.")],
         related=["compress-image", "heic-to-jpg", "resize-image", "convert-image"]),
    dict(slug="jpg-to-pdf", tool="imagestopdf", name="JPG to PDF", ex="12 JPG→1 PDF", tile="Photos or scans into one PDF, in your order.",
         title="JPG to PDF: Combine Images into One PDF, Free | VKonvert",
         desc="Turn JPG, PNG and HEIC images into one PDF. Choose A4 or Letter, page direction and margins. Free, in your browser, no upload.",
         h1="JPG to PDF",
         lead="Turn photos, scans or screenshots into one PDF, in the order you want. Nothing is uploaded.",
         steps=[("Add pictures", "Drop them or tap Choose pictures."), ("Put them in order", "Drag the cards on the left. Pick A4, Letter or Fit picture."), ("Make the PDF", "Tap Make PDF and save it.")],
         faqs=[("Can I change the page order?", "Yes. Drag the pictures on the left into the order you want before making the PDF."),
               ("Does it work with iPhone photos?", "Yes, HEIC photos are converted automatically."),
               ("Which page size should I pick?", "A4 for most of the world, Letter for the US and Canada, or Fit picture to make each page the shape of its photo.")],
         related=["merge-pdf", "compress-pdf", "pdf-to-jpg", "heic-to-jpg"]),
    dict(slug="merge-pdf", tool="merge", name="Merge PDF", ex="3→1 PDF", tile="Combine 2, 3 or 50 PDFs into one file.",
         title="Merge PDF Files Online Free, No Upload | VKonvert",
         desc="Combine PDF files into one in your browser. Drag to set the order. Free, private, no upload and no sign-up.",
         h1="Merge PDF files", lead="Combine PDFs into one file, in the order you choose. Nothing is uploaded.",
         steps=[("Add PDFs", "Drop two or more PDFs, or tap Choose PDFs."), ("Set the order", "Drag the cards on the left."), ("Merge", "Tap Merge and save the new PDF.")],
         faqs=[("Is there a limit?", "No fixed limit. Very large PDFs depend on your device’s memory."),
               ("Can I merge locked PDFs?", "Not while they’re locked. Remove the password first."),
               ("Are my PDFs uploaded?", "No. They’re combined on your device.")],
         related=["split-pdf", "compress-pdf", "jpg-to-pdf", "rotate-pdf"]),
    dict(slug="split-pdf", tool="split", name="Split PDF", ex="1–3, 7", tile="Take out just the pages you need.",
         title="Split PDF: Extract Pages Online Free | VKonvert",
         desc="Extract pages from a PDF, split it into ranges, or into single pages. Free, in your browser, no upload.",
         h1="Split PDF", lead="Take out the pages you need, split by ranges, or save every page as its own PDF.",
         steps=[("Add a PDF", "Drop it or tap Choose PDFs."), ("Pick the pages", "Type something like 1-3, 7, or choose Every page."), ("Save", "Save the new PDFs, or all as a ZIP.")],
         faqs=[("How do I type pages?", "Use commas and dashes: 1-3, 7, 10- means pages 1 to 3, page 7, and page 10 to the end."),
               ("What’s the difference between the options?", "Pick pages makes one PDF with those pages; One file per range makes a PDF for each range; Every page makes one PDF per page.")],
         related=["merge-pdf", "rotate-pdf", "compress-pdf", "pdf-to-jpg"]),
    dict(slug="compress-pdf", tool="compresspdf", name="Compress PDF", ex="18 MB→4 MB", tile="Make scanned PDFs small enough to email.",
         title="Compress PDF Online Free, No Upload | VKonvert",
         desc="Reduce PDF file size in your browser. Great for scanned documents and photo PDFs. Text stays sharp. Free, private.",
         h1="Compress PDF", lead="Make PDFs small enough for email and upload forms. Text stays sharp and searchable. Nothing is uploaded.",
         steps=[("Add PDFs", "Drop them or tap Choose PDFs."), ("Pick a level", "Light, Balanced or Strong."), ("Save", "Save the smaller PDF.")],
         faqs=[("How much smaller will it get?", "Scanned and photo-heavy PDFs often shrink by 50–90%. PDFs that are mostly text are already small and change less."),
               ("Does text get blurry?", "No. Only the pictures inside are recompressed; text and drawings stay as they are."),
               ("What if it can’t shrink my PDF?", "You get your original back rather than a bigger file.")],
         related=["merge-pdf", "jpg-to-pdf", "split-pdf", "compress-image"]),
    dict(slug="pdf-to-jpg", tool="pdftojpg", name="PDF to JPG", ex="PDF→JPG", tile="Save every page as a JPG or PNG.",
         title="PDF to JPG: Convert PDF Pages to Images, Free | VKonvert",
         desc="Turn each page of a PDF into a JPG or PNG at 96, 150 or 300 dpi. Free, in your browser, no upload.",
         h1="PDF to JPG", lead="Save every page of a PDF as a picture, for sharing, slides or social posts.",
         steps=[("Add a PDF", "Drop it or tap Choose PDFs."), ("Pick JPG or PNG and sharpness", "150 dpi is good for screens, 300 dpi for printing."), ("Save", "Save each page, or all as a ZIP.")],
         faqs=[("JPG or PNG?", "JPG files are smaller. PNG is best for pages with sharp text and lines."),
               ("Can I do this for many pages?", "Yes. Every page becomes a picture; Download all puts them in one ZIP.")],
         related=["jpg-to-pdf", "split-pdf", "compress-pdf", "merge-pdf"]),
    dict(slug="rotate-pdf", tool="rotate", name="Rotate & reorder PDF", ex="↻ 90°", tile="Fix sideways pages and drag them into order.",
         title="Rotate PDF Pages and Reorder Them, Free | VKonvert",
         desc="Rotate sideways PDF pages, move pages around or delete them. See every page as you edit. Free, in your browser, no upload.",
         h1="Rotate and reorder PDF pages", lead="Fix sideways scans, put pages in the right order, or remove the ones you don’t need.",
         steps=[("Add a PDF", "Drop it or tap Choose PDFs; its pages show up."), ("Pick pages and fix them", "Turn left or right, move earlier or later, or delete."), ("Save", "Tap Save the new PDF.")],
         faqs=[("Does rotating reduce quality?", "No. The pages are turned, not redrawn."), ("Can I select several pages?", "Yes. Tap each page you want, or use Select all.")],
         related=["split-pdf", "merge-pdf", "compress-pdf", "pdf-to-jpg"]),
]
BY_SLUG = {p["slug"]: p for p in PAGES}
TILES = ["convert-image", "compress-image", "resize-image", "remove-photo-info", "jpg-to-pdf", "merge-pdf", "split-pdf",
         "compress-pdf", "pdf-to-jpg", "rotate-pdf"]

e = html.escape


def head(title, desc, path, extra_ld=None):
    url = SITE + path
    ld = [{"@context": "https://schema.org", "@type": "WebApplication", "name": "VKonvert", "url": SITE + "/",
           "applicationCategory": "MultimediaApplication", "operatingSystem": "Any (browser), Windows",
           "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
           "author": {"@type": "Person", "name": "Vishnu VK"}}]
    if extra_ld:
        ld.append(extra_ld)
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{e(title)}</title>
<meta name="description" content="{e(desc)}">
<link rel="canonical" href="{url}">
<meta name="theme-color" content="#D9342B">
<meta property="og:type" content="website">
<meta property="og:site_name" content="VKonvert">
<meta property="og:title" content="{e(title)}">
<meta property="og:description" content="{e(desc)}">
<meta property="og:url" content="{url}">
<meta property="og:image" content="{SITE}/assets/og.png">
<meta name="twitter:card" content="summary_large_image">
<link rel="icon" href="/assets/favicon-32.png" sizes="32x32">
<link rel="apple-touch-icon" href="/assets/apple-touch-icon.png">
<link rel="manifest" href="/manifest.webmanifest">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:opsz,wght@12..96,700;12..96,800&family=Figtree:wght@400;500;600;700&family=JetBrains+Mono:wght@500;700&display=swap">
<link rel="stylesheet" href="/assets/vk.css">
<script>try{{var t=localStorage.getItem("vk-theme")}}catch(x){{}}document.documentElement.dataset.theme=t||(matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light");</script>
<script type="application/ld+json">{json.dumps(ld if len(ld) > 1 else ld[0])}</script>
</head>
<body>
"""


def header(active=None):
    def link(href, text):
        cur = ' aria-current="page"' if active == href else ""
        return f'<a href="{href}"{cur}>{text}</a>'
    return f"""<header class="top">
  <div class="wrap">
    <a class="brand" href="/" aria-label="VKonvert home">{MARK}<span class="word"><b>VK</b>onvert</span></a>
    <nav class="nav" aria-label="Main">
      {link("/#tools", "All tools")}{link("/compress-image", "Compress")}{link("/jpg-to-pdf", "JPG to PDF")}{link("/merge-pdf", "Merge PDF")}{link("/download", "Windows app")}
    </nav>
    <div class="top-actions">
      <button class="round" type="button" data-theme-toggle aria-label="Switch light or dark mode">
        <svg class="moon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/></svg>
        <svg class="sun" viewBox="0 0 24 24" fill="none" stroke="#F0C26B" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>
      </button>
      <a class="pill-dark" href="/download"><span class="long">Get the&nbsp;</span>Windows app</a>
      <button class="round menu-btn" type="button" aria-label="Menu" aria-expanded="false">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16"/></svg>
      </button>
    </div>
  </div>
</header>
"""


FOOTER = f"""<footer class="foot">
  <div class="wrap">
    <span>VKonvert · Made by Vishnu VK · Made with Claude Code · Free to use (MIT)</span>
    <nav aria-label="Footer"><a href="/download">Windows app</a><a href="/privacy">Privacy</a><a href="{GITHUB}">GitHub</a><a href="/third-party-licenses.txt">Licenses</a></nav>
  </div>
</footer>
<script src="/assets/vk.js" defer></script>
<script>if("serviceWorker"in navigator)addEventListener("load",function(){{navigator.serviceWorker.register("/sw.js").catch(function(){{}})}});</script>
</body>
</html>
"""


def machine(tool, target=None):
    t = f' data-target="{target}"' if target else ""
    return (f'<div id="machine" data-tool="{tool}"{t}><noscript><p style="padding:24px">VKonvert needs JavaScript to work on your files '
            f'inside your browser. Or <a href="/download">get the Windows app</a>.</p></noscript></div>')


def tiles(slugs, hot_first=True):
    out = []
    for i, s in enumerate(slugs):
        p = BY_SLUG[s]
        cls = "tile hot" if hot_first and i == 0 else "tile"
        out.append(f'<a class="{cls}" href="/{s}"><span class="ex">{e(p["ex"])}</span><span class="gap"></span>'
                   f'<strong>{e(p["name"])}</strong><span class="d">{e(p["tile"])}</span></a>')
    out.append('<a class="tile" href="/download"><span class="ex">••••</span><span class="gap"></span><strong>Protect PDF</strong>'
               '<span class="d">Lock a PDF with a password (AES-256).</span><span class="app">In the Windows app</span></a>')
    return "\n".join(out)


def faq_html(faqs):
    items = "".join(f"<details><summary>{e(q)}</summary><p>{e(a)}</p></details>" for q, a in faqs)
    return f'<div class="faq">{items}</div>'


def faq_ld(faqs):
    return {"@context": "https://schema.org", "@type": "FAQPage",
            "mainEntity": [{"@type": "Question", "name": q, "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in faqs]}


PRIVACY_BAND = """<div class="band"><div class="wrap">
  <div class="l"><div class="pixtrail" aria-hidden="true"><i></i><i></i><i></i><i></i></div>
    <h2 class="h2">Your files never leave your device.</h2>
    <p class="lead">Most online converters upload your photos and documents to their servers. VKonvert does the work inside your browser, so there is nothing to upload and nothing to leak.</p></div>
  <div class="r"><div class="stat"><b>0</b><span>files uploaded</span></div><div class="stat"><b>₹0</b><span>no sign-up, no limits</span></div><div class="stat"><b>MIT</b><span>open-source license</span></div></div>
</div></div>"""

APP_BAND = f"""<section class="block"><div class="wrap appband">
  <div class="l"><span class="lbl" style="color:var(--red)">Windows app</span>
    <h2 class="h2">Right-click. Convert. Done.</h2>
    <p class="lead">The same tools on your PC, plus “VKonvert” in the right-click menu, saving as HEIC, AVIF and TIFF, PDF passwords, and light and dark mode.</p>
    <div class="row"><a class="btn-red" href="/download">Download for Windows</a><span class="mono" style="color:var(--muted)">Windows 10 / 11 · free</span></div></div>
  <div class="r"><img src="/assets/app-light.png" alt="The VKonvert Windows app converting four photos to JPG" width="1292" height="786" loading="lazy"></div>
</div></section>"""


def write(name, text):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(text)


def home():
    faqs = [("Are my files uploaded anywhere?", "No. VKonvert does all the work inside your browser or the Windows app. You can even turn off Wi-Fi after the page loads."),
            ("How do I convert iPhone HEIC photos to JPG?", "Drop the photos on the converter above, keep JPG selected and press Convert. You can do dozens at once."),
            ("How small can a PDF get?", "Scanned and photo-heavy PDFs often shrink by 50–90%. PDFs that are mostly text are already small and change less."),
            ("Is it really free?", "Yes. No account, no watermark and no limits. The code is public under the MIT license."),
            ("Is there an app?", "Yes, a free Windows app with the same tools plus a right-click menu. A Mac version is planned.")]
    page = head("VKonvert: Convert, Compress and Combine Images and PDFs, Free",
                "Free image and PDF tools that run in your browser: convert HEIC to JPG, compress images, JPG to PDF, merge, split and compress PDFs. No upload, no sign-up.",
                "/", faq_ld(faqs))
    page += header()
    page += f"""<main>
<section class="hero"><div class="wrap">
  <h1><span class="a">Any file in.</span><span class="b">The right file out.</span></h1>
  <p>Convert, compress and combine images and PDFs, right in your browser. Free, no sign-up, and nothing is uploaded.</p>
</div></section>
<section aria-label="Image converter"><div class="wrap">{machine("convert", "JPG")}</div></section>
<section class="block" id="tools"><div class="wrap">
  <div class="row" style="justify-content:space-between;align-items:flex-end"><h2 class="h2">Eleven tools. One place.</h2><p class="sub2">Every tool works in the Windows app too.</p></div>
  <div class="tiles">{tiles(TILES)}</div>
  <h2 class="h2" style="margin-top:44px;font-size:clamp(24px,2.6vw,30px)">Popular conversions</h2>
  <div class="related">{"".join(f'<a href="/{s}">{e(BY_SLUG[s]["name"])}</a>' for s in ["heic-to-jpg", "png-to-jpg", "jpg-to-png", "webp-to-jpg", "jpg-to-webp", "jpg-to-pdf", "pdf-to-jpg"])}</div>
</div></section>
{PRIVACY_BAND}
{APP_BAND}
<section class="block"><div class="wrap"><h2 class="h2">Questions</h2>{faq_html(faqs)}</div></section>
</main>
"""
    write("index.html", page + FOOTER)


def tool_page(p):
    page = head(p["title"], p["desc"], "/" + p["slug"], faq_ld(p["faqs"]))
    page += header("/" + p["slug"])
    steps = "".join(f'<div><span class="n">{i + 1}</span><div><b>{e(a)}</b><span>{e(b)}</span></div></div>' for i, (a, b) in enumerate(p["steps"]))
    rel = "".join(f'<a href="/{s}">{e(BY_SLUG[s]["name"])}</a>' for s in p["related"])
    page += f"""<main>
<section class="page-head"><div class="wrap">
  <nav class="crumbs" aria-label="Breadcrumb"><a href="/#tools">Tools</a> › {e(p["name"])}</nav>
  <h1>{e(p["h1"])}</h1>
  <p>{e(p["lead"])}</p>
</div></section>
<section aria-label="{e(p["name"])}"><div class="wrap">{machine(p["tool"], p.get("target"))}</div></section>
<section class="block"><div class="wrap"><h2 class="h2">How to use {e(p["name"])}</h2><div class="how">{steps}</div></div></section>
<section class="block"><div class="wrap"><h2 class="h2">Questions</h2>{faq_html(p["faqs"])}</div></section>
<section class="block"><div class="wrap"><h2 class="h2" style="font-size:clamp(24px,2.6vw,30px)">Related tools</h2><div class="related">{rel}</div></div></section>
{PRIVACY_BAND}
{APP_BAND}
</main>
"""
    write(p["slug"] + ".html", page + FOOTER)


def download():
    page = head("Download VKonvert for Windows, Free | VKonvert",
                "Download the free VKonvert app for Windows 10 and 11: convert, compress and combine images and PDFs, with a right-click menu. Installer or portable.",
                "/download", {"@context": "https://schema.org", "@type": "SoftwareApplication", "name": "VKonvert", "operatingSystem": "Windows 10, Windows 11",
                              "applicationCategory": "MultimediaApplication", "softwareVersion": VERSION, "downloadUrl": SETUP,
                              "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"}, "author": {"@type": "Person", "name": "Vishnu VK"}})
    page += header("/download")
    page += f"""<main>
<section class="page-head"><div class="wrap">
  <h1>VKonvert for Windows</h1>
  <p>All eleven tools on your PC, plus the right-click menu, saving as HEIC, AVIF and TIFF, PDF passwords, and light and dark mode. Free, works offline.</p>
  <div class="dlcards">
    <div class="dlcard main"><span class="lbl" style="color:var(--red)">Recommended</span><h2>Installer</h2>
      <p>Accept the license, pick a folder and options, done. Adds the Start menu, desktop shortcut and right-click menu. Uninstall any time from Settings › Apps.</p>
      <span class="meta">VKonvert-Setup-{VERSION}.exe · 15.8 MB · no admin needed</span>
      <div><a class="btn-red" href="{SETUP}">Download installer</a></div></div>
    <div class="dlcard"><span class="lbl">No install</span><h2>Portable</h2>
      <p>Unzip anywhere, even a USB stick, and run VKonvert.exe. No shortcuts or right-click menu. Delete the folder to remove it.</p>
      <span class="meta">VKonvert-{VERSION}-Portable.zip · 15.6 MB</span>
      <div><a class="btn-line" href="{PORTABLE}">Download portable ZIP</a></div></div>
  </div>
</div></section>
<section class="block"><div class="wrap"><img src="/assets/app-dark.png" alt="VKonvert in dark mode compressing three pictures" width="1292" height="786" style="border-radius:12px;border:1px solid var(--line);display:block"></div></section>
<section class="block"><div class="wrap prose">
  <h2>Windows says “Windows protected your PC”</h2>
  <p>That message appears for apps that aren’t code-signed yet. Click <b>More info</b>, then <b>Run anyway</b>. VKonvert is open source: you can read every line on <a href="{GITHUB}">GitHub</a>.</p>
  <h2>Requirements</h2>
  <p>Windows 10 or 11, 64-bit. If Windows says the Visual C++ Runtime is missing, VKonvert offers Microsoft’s free download.</p>
  <h2>Mac</h2>
  <p>A Mac version is planned. Until then, every tool except PDF passwords works on this website in Safari or Chrome.</p>
</div></section>
</main>
"""
    write("download.html", page + FOOTER)


def privacy():
    page = head("Privacy | VKonvert", "VKonvert works on your files inside your browser or the Windows app. Nothing is uploaded and no personal data is collected.", "/privacy")
    page += header()
    page += f"""<main><section class="page-head"><div class="wrap prose">
  <h1>Privacy</h1>
  <p>Short version: your files never leave your device, and VKonvert doesn’t collect anything about you.</p>
  <h2>Your files</h2>
  <p>On this website, every tool runs inside your browser. The files you add are read and processed on your device and are never sent to a server. The Windows app works the same way, offline.</p>
  <h2>What this website stores</h2>
  <p>Only your light or dark mode choice, saved in your own browser. The site can also save itself in your browser so it opens without an internet connection. There are no accounts, no ads and no tracking cookies.</p>
  <h2>Hosting and fonts</h2>
  <p>The website is hosted on Vercel, and its fonts come from Google Fonts. Like any website host, they see standard request details such as your IP address when your browser loads the pages. They never see your files.</p>
  <h2>Contact</h2>
  <p>Questions or ideas: open an issue on <a href="{GITHUB}/issues">GitHub</a>.</p>
  <p class="mono">Updated {date.today().strftime("%-d %B %Y")}</p>
</div></section></main>
"""
    write("privacy.html", page + FOOTER)


def not_found():
    page = head("Page not found | VKonvert", "This page doesn’t exist. Try one of VKonvert’s free image and PDF tools.", "/404")
    page = page.replace('<link rel="canonical"', '<meta name="robots" content="noindex"><link rel="canonical"')
    page += header()
    page += f"""<main><section class="hero"><div class="wrap"><h1><span class="a">404.</span><span class="b">Page not found.</span></h1>
<p>That page doesn’t exist. Pick a tool instead:</p><div class="tiles">{tiles(TILES[:4])}</div></div></section></main>
"""
    write("404.html", page + FOOTER)


def extras():
    urls = ["/"] + ["/" + p["slug"] for p in PAGES] + ["/download", "/privacy"]
    today = date.today().isoformat()
    write("sitemap.xml", '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
          "".join(f"  <url><loc>{SITE}{u}</loc><lastmod>{today}</lastmod></url>\n" for u in urls) + "</urlset>\n")
    write("robots.txt", f"User-agent: *\nAllow: /\n\nSitemap: {SITE}/sitemap.xml\n")
    write("manifest.webmanifest", json.dumps({
        "name": "VKonvert", "short_name": "VKonvert", "description": "Convert, compress and combine images and PDFs.",
        "start_url": "/", "display": "standalone", "background_color": "#F7F3F2", "theme_color": "#D9342B",
        "icons": [{"src": "/assets/icon-192.png", "sizes": "192x192", "type": "image/png"},
                  {"src": "/assets/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "any maskable"}]}, indent=1))
    pages = ["/"] + ["/" + p["slug"] for p in PAGES] + ["/download", "/privacy"]
    assets = ["/assets/vk.css", "/assets/vk.js", "/assets/favicon-32.png", "/assets/icon-192.png"]
    write("sw.js", f"""// Keeps VKonvert working offline: pages and tools are cached the first time they're used.
const CACHE = "vk-{VERSION}-{today}";
const CORE = {json.dumps(pages + assets)};
self.addEventListener("install", e => e.waitUntil(caches.open(CACHE).then(c => c.addAll(CORE)).then(() => self.skipWaiting())));
self.addEventListener("activate", e => e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())));
self.addEventListener("fetch", e => {{
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin === location.origin && url.pathname.startsWith("/assets/lib/")) {{
    // Libraries never change for a given version: cache first.
    e.respondWith(caches.match(req).then(hit => hit || fetch(req).then(res => {{ const copy = res.clone(); caches.open(CACHE).then(c => c.put(req, copy)); return res; }})));
    return;
  }}
  // Everything else: network first, so updates show up; cache when offline.
  e.respondWith(fetch(req).then(res => {{
    if (res.ok && (url.origin === location.origin || url.hostname.endsWith("gstatic.com") || url.hostname.endsWith("googleapis.com"))) {{
      const copy = res.clone(); caches.open(CACHE).then(c => c.put(req, copy));
    }}
    return res;
  }}).catch(() => caches.match(req).then(hit => hit || caches.match(url.pathname + ".html"))));
}});
""")
    write("vercel.json", json.dumps({
        "cleanUrls": True, "trailingSlash": False,
        "headers": [
            {"source": "/assets/lib/(.*)", "headers": [{"key": "Cache-Control", "value": "public, max-age=31536000, immutable"}]},
            {"source": "/(.*)", "headers": [
                {"key": "X-Content-Type-Options", "value": "nosniff"},
                {"key": "Referrer-Policy", "value": "strict-origin-when-cross-origin"},
                {"key": "Permissions-Policy", "value": "camera=(), microphone=(), geolocation=()"}]}]}, indent=1))


if __name__ == "__main__":
    home()
    for p in PAGES:
        tool_page(p)
    download()
    privacy()
    not_found()
    extras()
    print("wrote", len(PAGES) + 4, "pages to", OUT, "for", SITE)
