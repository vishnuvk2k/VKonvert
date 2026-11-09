/* VKonvert website: every tool runs in the browser. Nothing is uploaded. */
(() => {
"use strict";

// ───────────── Theme and menu ─────────────
const root = document.documentElement;
document.querySelectorAll("[data-theme-toggle]").forEach(b => b.addEventListener("click", () => {
  const dark = root.dataset.theme !== "dark";
  root.dataset.theme = dark ? "dark" : "light";
  try { localStorage.setItem("vk-theme", root.dataset.theme); } catch (e) {}
  b.setAttribute("aria-label", dark ? "Switch to light mode" : "Switch to dark mode");
}));
const menuBtn = document.querySelector(".menu-btn");
if (menuBtn) menuBtn.addEventListener("click", () => {
  const nav = document.querySelector(".nav");
  const open = nav.classList.toggle("open");
  menuBtn.setAttribute("aria-expanded", open);
});

const mount = document.getElementById("machine");
if (!mount) return;

// ───────────── Small helpers ─────────────
function h(tag, attrs, ...kids) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === "class") e.className = v;
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "text") e.textContent = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const k of kids.flat()) if (k != null && k !== false) e.append(k.nodeType ? k : document.createTextNode(k));
  return e;
}
const size = b => b >= 1048576 ? (b / 1048576).toFixed(1) + " MB" : b >= 1024 ? Math.round(b / 1024) + " KB" : b + " B";
const change = (a, b) => { const p = Math.round((b - a) * 100 / a); return p < 0 ? "−" + (-p) + "%" : p === 0 ? "±0%" : "+" + p + "%"; };
const plural = (n, one, many) => n + " " + (n === 1 ? one : (many || one + "s"));
const ext = name => (name.match(/\.([^.]+)$/) || [, ""])[1].toLowerCase();
const base = name => name.replace(/\.[^.]+$/, "");
const fmtName = name => ({ jpg: "JPG", jpeg: "JPG", jfif: "JPG", png: "PNG", webp: "WebP", heic: "HEIC", heif: "HEIC", gif: "GIF", bmp: "BMP", tif: "TIFF", tiff: "TIFF", avif: "AVIF", ico: "ICO", pdf: "PDF" })[ext(name)] || ext(name).toUpperCase();
const isPdf = f => ext(f.name) === "pdf" || f.type === "application/pdf";
const IMAGE_EXT = ["jpg", "jpeg", "jfif", "png", "webp", "heic", "heif", "gif", "bmp", "tif", "tiff", "avif", "ico"];
const isImage = f => IMAGE_EXT.includes(ext(f.name)) || (f.type || "").startsWith("image/");
const tick = () => new Promise(r => setTimeout(r, 0));

const loaded = {};
function lib(src) {
  if (!loaded[src]) loaded[src] = new Promise((ok, fail) => {
    const s = document.createElement("script");
    s.src = "/assets/lib/" + src; s.onload = ok; s.onerror = () => fail(new Error("Couldn’t load part of VKonvert. Check your connection and try again."));
    document.head.append(s);
  });
  return loaded[src];
}
const pdfLib = () => lib("pdf-lib.min.js").then(() => window.PDFLib);
async function pdfjs() {
  await lib("pdf.min.js");
  window.pdfjsLib.GlobalWorkerOptions.workerSrc = "/assets/lib/pdf.worker.min.js";
  return window.pdfjsLib;
}

// ───────────── Pictures in and out ─────────────
async function decode(file) {
  const e = ext(file.name);
  let blob = file;
  if (e === "heic" || e === "heif") {
    await lib("heic2any.min.js");
    const out = await window.heic2any({ blob: file, toType: "image/png" });
    blob = Array.isArray(out) ? out[0] : out;
  } else if (e === "tif" || e === "tiff") {
    await lib("pako.min.js"); await lib("UTIF.js");
    const buf = await file.arrayBuffer();
    const ifds = window.UTIF.decode(buf);
    window.UTIF.decodeImage(buf, ifds[0]);
    const rgba = window.UTIF.toRGBA8(ifds[0]);
    const c = canvas(ifds[0].width, ifds[0].height);
    c.getContext("2d").putImageData(new ImageData(new Uint8ClampedArray(rgba.buffer), ifds[0].width, ifds[0].height), 0, 0);
    return c;
  }
  try {
    return await createImageBitmap(blob, { imageOrientation: "from-image" });
  } catch (err) {
    const url = URL.createObjectURL(blob);
    try {
      const img = new Image(); img.src = url; await img.decode(); return img;
    } catch (e2) {
      throw new Error("This picture can’t be opened in your browser. Try the Windows app.");
    } finally { URL.revokeObjectURL(url); }
  }
}
const W = s => s.naturalWidth || s.width, H = s => s.naturalHeight || s.height;
function canvas(w, h) { const c = document.createElement("canvas"); c.width = Math.max(1, Math.round(w)); c.height = Math.max(1, Math.round(h)); return c; }

/** Draws a picture at a new size; big reductions go in halving steps so it stays sharp. */
function draw(src, w, h, bg) {
  let cur = src, cw = W(src), ch = H(src);
  while (cw / 2 >= w && ch / 2 >= h) {
    const step = canvas(cw / 2, ch / 2);
    const x = step.getContext("2d"); x.imageSmoothingQuality = "high"; x.drawImage(cur, 0, 0, step.width, step.height);
    cur = step; cw = step.width; ch = step.height;
  }
  const c = canvas(w, h), x = c.getContext("2d");
  if (bg) { x.fillStyle = bg; x.fillRect(0, 0, c.width, c.height); }
  x.imageSmoothingQuality = "high";
  x.drawImage(cur, 0, 0, c.width, c.height);
  return c;
}
function hasAlpha(c) {
  const d = c.getContext("2d").getImageData(0, 0, c.width, c.height).data;
  const step = Math.max(4, Math.floor(d.length / 4 / 40000) * 4);
  for (let i = 3; i < d.length; i += step) if (d[i] < 250) return true;
  return false;
}
function toBlob(c, type, q) {
  return new Promise((ok, fail) => c.toBlob(b => {
    if (!b) return fail(new Error("Your browser couldn’t create this file."));
    if (b.type !== type) return fail(new Error((type === "image/webp" ? "WebP" : type) + " saving isn’t supported in this browser. Try Chrome or Edge, or the Windows app."));
    ok(b);
  }, type, q));
}
function bmp(c) {
  const w = c.width, hgt = c.height, d = c.getContext("2d").getImageData(0, 0, w, hgt).data;
  const row = Math.ceil(w * 3 / 4) * 4, sizeAll = 54 + row * hgt, buf = new ArrayBuffer(sizeAll), v = new DataView(buf);
  v.setUint16(0, 0x4D42, true); v.setUint32(2, sizeAll, true); v.setUint32(10, 54, true);
  v.setUint32(14, 40, true); v.setInt32(18, w, true); v.setInt32(22, hgt, true); v.setUint16(26, 1, true); v.setUint16(28, 24, true);
  v.setUint32(34, row * hgt, true); v.setInt32(38, 2835, true); v.setInt32(42, 2835, true);
  const out = new Uint8Array(buf);
  for (let y = 0; y < hgt; y++) for (let x = 0; x < w; x++) {
    const s = ((hgt - 1 - y) * w + x) * 4, o = 54 + y * row + x * 3;
    out[o] = d[s + 2]; out[o + 1] = d[s + 1]; out[o + 2] = d[s];
  }
  return new Blob([buf], { type: "image/bmp" });
}
async function ico(src) {
  // Square, padded with transparency, with the usual Windows icon sizes stored as PNGs.
  const side = Math.max(W(src), H(src));
  const sq = canvas(side, side);
  sq.getContext("2d").drawImage(src, (side - W(src)) / 2, (side - H(src)) / 2);
  const sizes = [256, 64, 48, 32, 16];
  const pngs = [];
  for (const s of sizes) pngs.push(new Uint8Array(await (await toBlob(draw(sq, s, s), "image/png")).arrayBuffer()));
  const head = 6 + 16 * sizes.length;
  const total = head + pngs.reduce((a, p) => a + p.length, 0);
  const out = new Uint8Array(total), v = new DataView(out.buffer);
  v.setUint16(2, 1, true); v.setUint16(4, sizes.length, true);
  let off = head;
  sizes.forEach((s, i) => {
    const e = 6 + 16 * i;
    out[e] = s >= 256 ? 0 : s; out[e + 1] = s >= 256 ? 0 : s; v.setUint16(e + 4, 1, true); v.setUint16(e + 6, 32, true);
    v.setUint32(e + 8, pngs[i].length, true); v.setUint32(e + 12, off, true);
    out.set(pngs[i], off); off += pngs[i].length;
  });
  return new Blob([out], { type: "image/x-icon" });
}

const A4 = [595.28, 841.89], LETTER = [612, 792];

async function jpegBytes(src, maxSide, q) {
  let w = W(src), hh = H(src);
  const s = Math.min(1, maxSide / Math.max(w, hh));
  return new Uint8Array(await (await toBlob(draw(src, w * s, hh * s, "#fff"), "image/jpeg", q)).arrayBuffer());
}

// ───────────── Tools ─────────────
const TARGETS = [
  { n: "JPG", type: "image/jpeg", ext: "jpg", q: true, alpha: false },
  { n: "PNG", type: "image/png", ext: "png", q: false, alpha: true },
  { n: "WebP", type: "image/webp", ext: "webp", q: true, alpha: true },
  { n: "PDF", ext: "pdf", q: true, alpha: false },
  { n: "ICO", ext: "ico", q: false, alpha: true },
  { n: "BMP", ext: "bmp", q: false, alpha: false },
];

function keys(labels, cols, pick, selected) {
  const box = h("div", { class: "keys", style: `grid-template-columns: repeat(${cols}, minmax(0, 1fr))`, role: "group" });
  const btns = labels.map((l, i) => h("button", { class: "key", type: "button", "aria-pressed": String(i === selected), onclick: () => select(i, true) }, l));
  function select(i, raise) { btns.forEach((b, j) => b.setAttribute("aria-pressed", String(j === i))); if (raise) pick(i); }
  box.append(...btns);
  box.select = select; box.buttons = btns;
  return box;
}
function qualityCard(value, onChange) {
  const hint = q => q >= 92 ? "Best quality, bigger files" : q >= 75 ? "Looks the same as the original" : q >= 55 ? "Good for sharing and websites" : q >= 35 ? "Small files, a little soft" : "Smallest files, visibly softer";
  const v = h("span", { class: "v" }, String(value)), hh = h("span", { class: "h" }, hint(value));
  const px = h("div", { class: "pixels", role: "slider", tabindex: "0", "aria-label": "Quality", "aria-valuemin": "10", "aria-valuemax": "100", "aria-valuenow": String(value) });
  const cells = Array.from({ length: 10 }, () => h("i"));
  px.append(...cells);
  function set(q) {
    value = Math.max(10, Math.min(100, Math.round(q / 5) * 5));
    v.textContent = value; hh.textContent = hint(value); px.setAttribute("aria-valuenow", value);
    cells.forEach((c, i) => { const f = value / 10 - i; c.className = f >= 1 ? "on" : f > 0 ? "half" : ""; });
    onChange(value);
  }
  const fromX = e => { const r = px.getBoundingClientRect(); set((e.clientX - r.left) / r.width * 100); };
  px.addEventListener("pointerdown", e => { px.setPointerCapture(e.pointerId); fromX(e); });
  px.addEventListener("pointermove", e => { if (e.buttons) fromX(e); });
  px.addEventListener("keydown", e => { if (e.key === "ArrowLeft") set(value - 5); if (e.key === "ArrowRight") set(value + 5); });
  set(value);
  return h("div", { class: "qcard" }, h("div", { class: "qhead" }, h("b", {}, "Quality"), v, hh), px, h("div", { class: "scale" }, h("span", {}, "Smaller file"), h("span", {}, "Better quality")));
}
function field(value, label, suffix, opts = {}) {
  const input = h("input", { value, "aria-label": label, type: opts.type || "text", inputmode: opts.inputmode, placeholder: opts.placeholder });
  const f = h("label", { class: "field" }, input, suffix ? h("span", { class: "suffix" }, suffix) : null);
  f.input = input;
  return f;
}
function toggle(text, on, change) {
  const input = h("input", { type: "checkbox", onchange: () => change(input.checked) });
  input.checked = on;
  return h("label", { class: "switch" }, input, h("span", { class: "track", "aria-hidden": "true" }), text);
}
const title = t => h("p", { class: "opt-title" }, t);
const info = (b, t) => h("div", { class: "infocard" }, h("b", {}, b), t);

/** Runs work for each file, showing progress on its card. */
async function perFile(files, ctx, work) {
  for (const f of files) ctx.state(f, "waiting");
  for (const f of files) {
    ctx.state(f, "working", .3);
    await tick();
    try {
      const outs = await work(f);
      for (const o of outs) ctx.output(o.name, o.blob, o.compare === false ? null : f);
      ctx.state(f, "done");
    } catch (err) {
      console.error(err);
      ctx.state(f, "failed", 0, err.message || String(err));
    }
  }
}
async function together(files, ctx, work) {
  for (const f of files) ctx.state(f, "working", .2);
  try {
    const outs = await work(i => files[i] && ctx.state(files[i], "done"));
    for (const o of outs) ctx.output(o.name, o.blob, null);
    for (const f of files) ctx.state(f, "done");
  } catch (err) {
    console.error(err);
    for (const f of files) ctx.state(f, "failed", 0, err.message || String(err));
  }
}

async function loadPdf(f, PDFLib) {
  try {
    return await PDFLib.PDFDocument.load(await f.file.arrayBuffer());
  } catch (err) {
    if (/encrypt/i.test(err.message)) throw new Error("This PDF is locked with a password. Unlock it first.");
    throw new Error("This file isn’t a PDF VKonvert can read.");
  }
}
const pdfBlob = bytes => new Blob([bytes], { type: "application/pdf" });

/** ASCII85 (PDF "ASCII85Decode") to bytes. */
function a85(input) {
  const out = [];
  let group = [], i = 0;
  const text = typeof input === "string" ? input : new TextDecoder("latin1").decode(input);
  for (; i < text.length; i++) {
    const ch = text[i];
    if (ch === "~") break;
    if (/\s/.test(ch)) continue;
    if (ch === "z" && group.length === 0) { out.push(0, 0, 0, 0); continue; }
    group.push(ch.charCodeAt(0) - 33);
    if (group.length === 5) {
      let v = 0; for (const g of group) v = v * 85 + g;
      out.push((v >>> 24) & 255, (v >>> 16) & 255, (v >>> 8) & 255, v & 255);
      group = [];
    }
  }
  if (group.length) {
    const n = group.length;
    while (group.length < 5) group.push(84);
    let v = 0; for (const g of group) v = v * 85 + g;
    const bytes = [(v >>> 24) & 255, (v >>> 16) & 255, (v >>> 8) & 255, v & 255];
    out.push(...bytes.slice(0, n - 1));
  }
  return new Uint8Array(out);
}

function parseRanges(text, count) {
  const out = [];
  for (const raw of (text || "").split(/[,;]/)) {
    const part = raw.trim().replace(/[–—]/g, "-");
    if (!part) continue;
    let a, b;
    const m = part.match(/^(\d*)\s*-\s*(\d*)$/);
    if (m) { a = m[1] ? +m[1] : 1; b = m[2] ? +m[2] : count; }
    else if (/^\d+$/.test(part)) a = b = +part;
    else throw new Error(`“${part}” isn’t a page number or range. Use something like 1-3, 7.`);
    if (a < 1 || b < 1 || a > count || b > count) throw new Error(`“${part}” is outside this PDF, which has ${plural(count, "page")}.`);
    if (b < a) [a, b] = [b, a];
    out.push(Array.from({ length: b - a + 1 }, (_, i) => a - 1 + i));
  }
  if (!out.length) throw new Error("Type the pages you want, for example 1-3, 7.");
  return out;
}

function dominant(files) {
  if (!files.length) return ["Any", ""];
  const counts = {};
  files.forEach(f => { const n = fmtName(f.name); counts[n] = (counts[n] || 0) + 1; });
  const order = Object.keys(counts).sort((a, b) => counts[b] - counts[a]);
  return [order[0], order.length > 1 ? "+ " + order.slice(1, 4).join(", ") : ""];
}

const TOOLS = {
  convert: {
    accept: "image",
    init(o) { this.t = TARGETS.find(t => t.n === (o.target || "JPG")) || TARGETS[0]; this.q = 85; this.bg = "#ffffff"; },
    build(core, ctx) {
      const k = keys(TARGETS.map(t => t.n), 6, i => { this.t = TARGETS[i]; refresh(); ctx.changed(); }, TARGETS.indexOf(this.t));
      const q = qualityCard(this.q, v => this.q = v);
      const sw = [["#ffffff", "White"], ["#000000", "Black"]].map(([c, n], i) =>
        h("button", { class: "swatch", type: "button", style: `background:${c}`, "aria-label": n, "aria-pressed": String(i === 0),
          onclick: e => { this.bg = c; sw.forEach(s => s.setAttribute("aria-pressed", String(s === e.currentTarget))); } }));
      const bg = h("div", { class: "row" }, h("span", { class: "muted" }, "See-through parts become"), h("div", { class: "swatches" }, sw));
      const note = h("p", { class: "hint" }, "Need HEIC, AVIF, TIFF or GIF? Those save in the Windows app.");
      const refresh = () => { q.hidden = !this.t.q; bg.hidden = this.t.alpha; };
      refresh();
      core.append(k, q, bg, note);
    },
    display(files) { const [f, o] = dominant(files); return [f, this.t.n, o, ""]; },
    action(files) { return files.length ? `Convert ${plural(files.length, "file")} → ${this.t.n}` : `Convert → ${this.t.n}`; },
    async run(files, ctx) {
      const t = this.t, q = this.q / 100, bg = this.bg;
      await perFile(files, ctx, async f => {
        const src = await decode(f.file);
        let blob;
        if (t.n === "PDF") {
          const PDFLib = await pdfLib();
          const doc = await PDFLib.PDFDocument.create();
          const img = await doc.embedJpg(await jpegBytes(src, 4000, q));
          const page = doc.addPage([img.width * .75, img.height * .75]);
          page.drawImage(img, { x: 0, y: 0, width: page.getWidth(), height: page.getHeight() });
          blob = pdfBlob(await doc.save());
        } else if (t.n === "ICO") blob = await ico(src);
        else {
          const c = draw(src, W(src), H(src), t.alpha ? null : bg);
          blob = t.n === "BMP" ? bmp(c) : await toBlob(c, t.type, t.q ? q : undefined);
        }
        return [{ name: base(f.name) + "." + t.ext, blob }];
      });
    },
  },

  compress: {
    accept: "image",
    init() { this.mode = "size"; this.kb = 500; this.q = 70; this.strip = true; },
    build(core, ctx) {
      const mode = keys(["Size limit", "Quality"], 2, i => { this.mode = i ? "quality" : "size"; refresh(); ctx.changed(); }, 0);
      const t = title("Each picture under");
      const sizes = [50, 100, 200, 500, 1024];
      const k = keys(["50 KB", "100 KB", "200 KB", "500 KB", "1 MB"], 5, i => { this.kb = sizes[i]; custom.input.value = ""; ctx.changed(); }, 3);
      const custom = field("", "Custom size in KB", "KB", { inputmode: "numeric", placeholder: "Or type a size" });
      custom.input.addEventListener("input", () => { const v = parseInt(custom.input.value, 10); if (v > 0) { this.kb = v; k.select(-1); } ctx.changed(); });
      const q = qualityCard(this.q, v => { this.q = v; ctx.changed(); });
      const refresh = () => { const s = this.mode === "size"; t.hidden = k.hidden = custom.hidden = !s; q.hidden = s; };
      refresh();
      core.append(mode, t, k, custom, q,
        h("p", { class: "hint" }, "Photos stay JPG/WebP, PNGs stay PNG with their see-through parts. Location and camera details are removed too."));
    },
    display(files) {
      const total = files.reduce((a, f) => a + f.size, 0);
      return files.length ? [size(total), this.mode === "size" ? size(this.kb * 1024) : "Q " + this.q, plural(files.length, "picture"), this.mode === "size" ? "max, each" : ""] : ["Big", "Small", "", ""];
    },
    action(files) { return files.length ? `Compress ${plural(files.length, "picture")}` : "Compress"; },
    async run(files, ctx) {
      const target = this.kb * 1024, byQ = this.mode === "quality", q = this.q / 100;
      await perFile(files, ctx, async f => {
        const src = await decode(f.file);
        let c = draw(src, W(src), H(src));
        const e = fmtName(f.name), alpha = hasAlpha(c);
        const kind = e === "PNG" || (alpha && e !== "WebP") ? "png" : e === "WebP" ? "webp" : "jpg";
        if (kind === "jpg") c = draw(c, c.width, c.height, "#fff");
        const enc = async (cv, qq) => kind === "png" ? png(cv, qq) : toBlob(cv, kind === "webp" ? "image/webp" : "image/jpeg", qq);
        let blob;
        if (byQ) blob = await enc(c, q);
        else {
          for (let round = 0; round < 8 && !blob; round++) {
            let lo = .1, hi = .92, fit = null, last;
            for (let i = 0; i < 7; i++) {
              const mid = (lo + hi) / 2; last = await enc(c, mid);
              if (last.size <= target) { fit = last; lo = mid; } else hi = mid;
            }
            if (fit) { blob = fit; break; }
            const small = await enc(c, .1);
            if (small.size <= target) { blob = small; break; }
            const r = Math.max(.35, Math.min(.9, Math.sqrt(target / small.size) * .92));
            c = draw(c, c.width * r, c.height * r);
            if (round === 7) blob = small;
          }
        }
        if (blob.size >= f.size && kind === ext(f.name).replace("jpeg", "jpg")) blob = f.file; // already small: keep it
        return [{ name: base(f.name) + "-compressed." + kind, blob }];
      });
      async function png(cv, qq) {
        await lib("pako.min.js"); await lib("UPNG.js");
        const d = cv.getContext("2d").getImageData(0, 0, cv.width, cv.height).data;
        const colors = qq >= .9 ? 0 : qq >= .7 ? 256 : qq >= .4 ? 128 : 64;
        return new Blob([window.UPNG.encode([d.buffer], cv.width, cv.height, colors)], { type: "image/png" });
      }
    },
  },

  resize: {
    accept: "image",
    init() { this.pick(2); this.grow = false; },
    pick(i) {
      const L = [1080, 1280, 1920, 3840];
      if (i < 4) Object.assign(this, { mode: "long", px: L[i], label: L[i] + "px" });
      else if (i < 6) Object.assign(this, { mode: "pct", pct: i === 4 ? 75 : 50, label: (i === 4 ? 75 : 50) + "pct" });
      else if (i === 6) Object.assign(this, { mode: "exact", w: 1080, hh: 1080, label: "square" });
      else Object.assign(this, { mode: "exact", w: 413, hh: 531, label: "passport" }); // 35 × 45 mm at 300 dpi
    },
    build(core, ctx) {
      const k = keys(["1080 px", "1280 px", "1920 px", "3840 px", "75%", "50%", "Square 1080", "Passport"], 4, i => { this.pick(i); custom.input.value = ""; ctx.changed(); }, 2);
      const custom = field("", "Long side in pixels", "px", { inputmode: "numeric", placeholder: "Or type a long side" });
      custom.input.addEventListener("input", () => { const v = parseInt(custom.input.value, 10); if (v >= 16 && v <= 20000) { Object.assign(this, { mode: "long", px: v, label: v + "px" }); k.select(-1); } ctx.changed(); });
      core.append(k, custom, toggle("Don’t make small pictures bigger", true, v => this.grow = !v),
        h("p", { class: "hint" }, "Square and Passport crop from the centre to fill the exact size. Passport is 35 × 45 mm at 300 dpi."));
    },
    display(files) {
      const to = this.mode === "long" ? this.px + " px" : this.mode === "pct" ? this.pct + "%" : `${this.w}×${this.hh}`;
      const f = files.find(x => x.w);
      return [f ? `${f.w}×${f.h}` : files.length ? "…" : "Any", to, files.length > 1 ? `+ ${files.length - 1} more` : "", this.mode === "long" ? "long side" : ""];
    },
    action(files) { return files.length ? `Resize ${plural(files.length, "picture")}` : "Resize"; },
    async run(files, ctx) {
      await perFile(files, ctx, async f => {
        const src = await decode(f.file);
        const w = W(src), hh = H(src);
        let c;
        if (this.mode === "exact") {
          const s = Math.max(this.w / w, this.hh / hh);
          const big = draw(src, w * s, hh * s);
          c = canvas(this.w, this.hh);
          c.getContext("2d").drawImage(big, (big.width - this.w) / 2, (big.height - this.hh) / 2, this.w, this.hh, 0, 0, this.w, this.hh);
        } else {
          let s = this.mode === "pct" ? this.pct / 100 : this.px / Math.max(w, hh);
          if (s > 1 && !this.grow) s = 1;
          c = draw(src, w * s, hh * s);
        }
        const kind = ["PNG", "WebP"].includes(fmtName(f.name)) ? fmtName(f.name) : "JPG";
        const t = TARGETS.find(x => x.n === kind);
        if (kind === "JPG") c = draw(c, c.width, c.height, "#fff");
        return [{ name: `${base(f.name)}-${this.label}.${t.ext}`, blob: await toBlob(c, t.type, .9) }];
      });
    },
  },

  removeinfo: {
    accept: "image",
    init() {},
    build(core) { core.append(info("What gets removed", "Location (GPS), camera and phone model, date taken, editing software and hidden thumbnails. The picture itself stays the same, and your original file isn’t changed.")); },
    display(files) { return ["Info", "Clean", "GPS · camera · date", files.length ? plural(files.length, "picture") : ""]; },
    action(files) { return files.length ? `Remove info from ${plural(files.length, "picture")}` : "Remove info"; },
    async run(files, ctx) {
      await perFile(files, ctx, async f => {
        const src = await decode(f.file);
        const kind = ["PNG", "WebP"].includes(fmtName(f.name)) ? fmtName(f.name) : "JPG";
        const t = TARGETS.find(x => x.n === kind);
        const c = draw(src, W(src), H(src), kind === "JPG" ? "#fff" : null);
        return [{ name: `${base(f.name)}-clean.${t.ext}`, blob: await toBlob(c, t.type, .93) }];
      });
    },
  },

  imagestopdf: {
    accept: "image", reorder: true,
    init() { this.size = "A4"; this.orient = 0; this.margin = 20; this.nameTouched = false; },
    build(core, ctx) {
      this.name = field("Images.pdf", "File name");
      this.name.input.addEventListener("input", () => this.nameTouched = true);
      core.append(title("Page size"), keys(["A4", "Letter", "Fit picture"], 3, i => this.size = ["A4", "Letter", "Fit"][i], 0),
        title("Page direction"), keys(["Auto", "Portrait", "Landscape"], 3, i => this.orient = i, 0),
        title("Margin"), keys(["None", "Small", "Large"], 3, i => this.margin = [0, 20, 48][i], 1),
        title("File name"), this.name,
        h("p", { class: "hint" }, "Drag the pictures on the left to change the page order."));
    },
    filesChanged(files) { if (!this.nameTouched && files[0]) this.name.input.value = base(files[0].name) + ".pdf"; },
    display(files) { return [files.length ? String(files.length) : "JPG", "PDF", files.length ? (files.length === 1 ? "picture" : "pictures") : "PNG, HEIC…", files.length ? plural(files.length, "page") : ""]; },
    action(files) { return files.length ? `Make 1 PDF from ${plural(files.length, "picture")}` : "Make PDF"; },
    validate() { return this.name.input.value.trim() ? null : "Give the PDF a file name."; },
    async run(files, ctx) {
      const PDFLib = await pdfLib();
      await together(files, ctx, async done => {
        const doc = await PDFLib.PDFDocument.create();
        for (let i = 0; i < files.length; i++) {
          const src = await decode(files[i].file);
          const img = await doc.embedJpg(await jpegBytes(src, 3600, .88));
          const m = this.margin, land = this.orient === 2 || (this.orient === 0 && img.width > img.height);
          let pw, ph;
          if (this.size === "Fit") { const s = A4[1] / Math.max(img.width, img.height); pw = img.width * s + 2 * m; ph = img.height * s + 2 * m; }
          else { const b = this.size === "A4" ? A4 : LETTER; [pw, ph] = land ? [b[1], b[0]] : b; }
          const page = doc.addPage([pw, ph]);
          const s = Math.min((pw - 2 * m) / img.width, (ph - 2 * m) / img.height);
          page.drawImage(img, { x: (pw - img.width * s) / 2, y: (ph - img.height * s) / 2, width: img.width * s, height: img.height * s });
          done(i);
          await tick();
        }
        let name = this.name.input.value.trim(); if (!/\.pdf$/i.test(name)) name += ".pdf";
        return [{ name, blob: pdfBlob(await doc.save()) }];
      });
    },
  },

  merge: {
    accept: "pdf", reorder: true,
    init() {},
    build(core) {
      this.name = field("Merged.pdf", "File name");
      core.append(title("File name"), this.name, h("p", { class: "hint" }, "Drag the PDFs on the left to change their order. Pages keep their own size."));
    },
    display(files) { const p = files.reduce((a, f) => a + (f.pages || 0), 0); return [files.length ? String(files.length) : "2+", "1 PDF", files.length === 1 ? "PDF" : "PDFs", p ? plural(p, "page") : ""]; },
    action(files) { return files.length < 2 ? "Merge PDFs" : `Merge ${files.length} PDFs into one`; },
    validate(files) { return files.length < 2 ? "Add at least two PDFs to merge." : this.name.input.value.trim() ? null : "Give the PDF a file name."; },
    async run(files, ctx) {
      const PDFLib = await pdfLib();
      await together(files, ctx, async done => {
        const out = await PDFLib.PDFDocument.create();
        for (let i = 0; i < files.length; i++) {
          const src = await loadPdf(files[i], PDFLib);
          (await out.copyPages(src, src.getPageIndices())).forEach(p => out.addPage(p));
          done(i);
        }
        let name = this.name.input.value.trim(); if (!/\.pdf$/i.test(name)) name += ".pdf";
        return [{ name, blob: pdfBlob(await out.save()) }];
      });
    },
  },

  split: {
    accept: "pdf",
    init() { this.mode = 0; },
    build(core, ctx) {
      this.ranges = field("1-2", "Pages to take");
      this.ranges.input.addEventListener("input", () => ctx.changed());
      const t = title("Pages"), n = h("p", { class: "hint" }, "Use commas and dashes: 1-3, 7, 10- means pages 1 to 3, page 7, and page 10 to the end.");
      core.append(keys(["Pick pages", "One file per range", "Every page"], 3, i => { this.mode = i; t.hidden = this.ranges.hidden = n.hidden = i === 2; ctx.changed(); }, 0), t, this.ranges, n);
    },
    count(files) {
      const p = files[0] && files[0].pages || 0;
      if (this.mode === 2) return p;
      if (this.mode === 0) return 1;
      try { return parseRanges(this.ranges.input.value, Math.max(1, p)).length; } catch (e) { return 0; }
    },
    display(files) { const p = files[0] && files[0].pages, n = this.count(files); return [p ? String(p) : "1 PDF", files.length && n ? String(n) : "Parts", p ? "pages" : "", files.length && n ? (n === 1 ? "PDF" : "PDFs") : ""]; },
    action() { return this.mode === 2 ? "Split into single pages" : this.mode === 1 ? "Split into files" : "Save these pages"; },
    validate(files) {
      if (this.mode === 2) return null;
      for (const f of files) { try { parseRanges(this.ranges.input.value, f.pages || 99999); } catch (e) { return (files.length > 1 ? f.name + ": " : "") + e.message; } }
      return null;
    },
    async run(files, ctx) {
      const PDFLib = await pdfLib();
      const text = this.ranges.input.value, mode = this.mode;
      await perFile(files, ctx, async f => {
        const src = await loadPdf(f, PDFLib);
        const n = src.getPageCount();
        let groups = mode === 2 ? Array.from({ length: n }, (_, i) => [i]) : parseRanges(text, n);
        if (mode === 0) groups = [groups.flat()];
        const outs = [];
        for (const g of groups) {
          const doc = await PDFLib.PDFDocument.create();
          (await doc.copyPages(src, g)).forEach(p => doc.addPage(p));
          const label = mode === 0 ? "pages " + text.replace(/\s/g, "") : g.length === 1 ? `page ${g[0] + 1}` : `pages ${g[0] + 1}-${g[g.length - 1] + 1}`;
          outs.push({ name: `${base(f.name)} (${label}).pdf`, blob: pdfBlob(await doc.save()), compare: false });
        }
        return outs;
      });
    },
  },

  compresspdf: {
    accept: "pdf",
    init() { this.level = 1; },
    build(core, ctx) {
      const notes = ["Light: pictures stay sharp enough to print.", "Balanced: good for email and uploads. Pictures stay clear on screen.", "Strong: smallest file. Pictures get softer, text stays sharp."];
      const n = h("p", { class: "hint" }, notes[1]);
      core.append(keys(["Light", "Balanced", "Strong"], 3, i => { this.level = i; n.textContent = notes[i]; ctx.changed(); }, 1), n,
        info("What to expect", "Scanned and photo-heavy PDFs often shrink by half or more. PDFs that are mostly text are already small and change less. Text always stays sharp and searchable."));
    },
    display(files) { const t = files.reduce((a, f) => a + f.size, 0); return [files.length ? size(t) : "Big", "Small", files.length ? plural(files.length, "PDF") : "PDF", ["light", "balanced", "strong"][this.level]]; },
    action(files) { return files.length ? `Compress ${plural(files.length, "PDF")}` : "Compress PDF"; },
    async run(files, ctx) {
      const PDFLib = await pdfLib();
      const [maxSide, q] = [[2400, .8], [1700, .65], [1200, .45]][this.level];
      const { PDFName, PDFRawStream, PDFNumber, PDFArray, PDFDict, PDFRef, decodePDFRawStream } = PDFLib;
      await perFile(files, ctx, async f => {
        const doc = await loadPdf(f, PDFLib);
        const ctxp = doc.context;
        const masks = new Set();
        for (const [, obj] of ctxp.enumerateIndirectObjects()) {
          const d = obj instanceof PDFRawStream ? obj.dict : obj instanceof PDFDict ? obj : null;
          const sm = d && d.get(PDFName.of("SMask"));
          if (sm instanceof PDFRef) masks.add(sm.toString());
        }
        const list = ctxp.enumerateIndirectObjects().filter(([ref, obj]) => obj instanceof PDFRawStream &&
          obj.dict.get(PDFName.of("Subtype")) === PDFName.of("Image") && !masks.has(ref.toString()));
        let i = 0;
        for (const [ref, stream] of list) {
          ctx.state(f, "working", .1 + .85 * (i++ / list.length));
          await tick();
          try { await squeeze(ref, stream); } catch (e) { /* leave this picture as it was */ }
        }
        const bytes = await doc.save({ useObjectStreams: true });
        return [{ name: base(f.name) + "-compressed.pdf", blob: bytes.length < f.size ? pdfBlob(bytes) : f.file }];

        async function squeeze(ref, stream) {
          const d = stream.dict, num = k => { const v = d.get(PDFName.of(k)); return v instanceof PDFNumber ? v.asNumber() : 0; };
          if (stream.contents.length < 20 * 1024 || d.get(PDFName.of("ImageMask")) || d.get(PDFName.of("Decode")) || d.get(PDFName.of("Mask"))) return;
          if (num("BitsPerComponent") !== 8) return;
          let filter = d.get(PDFName.of("Filter"));
          const filters = filter instanceof PDFArray ? filter.asArray().map(x => x.toString()) : filter ? [filter.toString()] : [];
          const w = num("Width"), hh = num("Height");
          let src;
          if (filters.length && filters[filters.length - 1] === "/DCTDecode" && filters.slice(0, -1).every(x => x === "/ASCII85Decode" || x === "/FlateDecode")) {
            // Undo transport layers (ASCII85, Flate) to reach the JPEG inside.
            let data = stream.contents;
            for (const fl of filters.slice(0, -1)) {
              if (fl === "/ASCII85Decode") data = a85(data);
              else { await lib("pako.min.js"); data = window.pako.inflate(data); }
            }
            src = await createImageBitmap(new Blob([data], { type: "image/jpeg" }));
          } else if (filters.every(x => x === "/FlateDecode") && !d.get(PDFName.of("DecodeParms"))) {
            const cs = d.get(PDFName.of("ColorSpace"));
            const comps = cs === PDFName.of("DeviceRGB") ? 3 : cs === PDFName.of("DeviceGray") ? 1 : 0;
            if (!comps) return;
            const raw = decodePDFRawStream(stream).decode();
            if (raw.length < w * hh * comps) return;
            const id = new ImageData(w, hh);
            for (let p = 0, s = 0; p < w * hh; p++, s += comps) {
              id.data[p * 4] = raw[s]; id.data[p * 4 + 1] = raw[s + (comps === 3 ? 1 : 0)]; id.data[p * 4 + 2] = raw[s + (comps === 3 ? 2 : 0)]; id.data[p * 4 + 3] = 255;
            }
            src = canvas(w, hh); src.getContext("2d").putImageData(id, 0, 0);
          } else return;
          const jpeg = await jpegBytes(src, maxSide, q);
          if (jpeg.length >= stream.contents.length * .9) return;
          const nd = d.clone(ctxp);
          nd.set(PDFName.of("Filter"), PDFName.of("DCTDecode"));
          nd.delete(PDFName.of("DecodeParms"));
          const s = Math.min(1, maxSide / Math.max(W(src), H(src)));
          nd.set(PDFName.of("Width"), PDFNumber.of(Math.round(W(src) * s)));
          nd.set(PDFName.of("Height"), PDFNumber.of(Math.round(H(src) * s)));
          nd.set(PDFName.of("Length"), PDFNumber.of(jpeg.length));
          const cs = nd.get(PDFName.of("ColorSpace"));
          if (cs === PDFName.of("DeviceGray")) nd.set(PDFName.of("ColorSpace"), PDFName.of("DeviceRGB"));
          ctxp.assign(ref, PDFRawStream.of(nd, jpeg));
        }
      });
    },
  },

  pdftojpg: {
    accept: "pdf",
    init() { this.png = false; this.dpi = 150; },
    build(core, ctx) {
      core.append(keys(["JPG", "PNG"], 2, i => { this.png = i === 1; ctx.changed(); }, 0), title("Sharpness"),
        keys(["Screen · 96 dpi", "Standard · 150 dpi", "Print · 300 dpi"], 3, i => this.dpi = [96, 150, 300][i], 1),
        h("p", { class: "hint" }, "Each page becomes one picture. Use “Download all” to get them in one ZIP."));
    },
    display(files) { const p = files.reduce((a, f) => a + (f.pages || 0), 0); return ["PDF", this.png ? "PNG" : "JPG", files.length ? plural(files.length, "file") : "", p ? plural(p, "picture") : ""]; },
    action() { return `Save pages as ${this.png ? "PNG" : "JPG"}`; },
    async run(files, ctx) {
      const lib = await pdfjs();
      await perFile(files, ctx, async f => {
        const doc = await lib.getDocument({ data: new Uint8Array(await f.file.arrayBuffer()) }).promise;
        const outs = [], digits = String(doc.numPages).length;
        for (let i = 1; i <= doc.numPages; i++) {
          const page = await doc.getPage(i);
          const vp = page.getViewport({ scale: this.dpi / 72 });
          const c = canvas(vp.width, vp.height), x = c.getContext("2d");
          x.fillStyle = "#fff"; x.fillRect(0, 0, c.width, c.height);
          await page.render({ canvasContext: x, viewport: vp }).promise;
          const blob = await toBlob(c, this.png ? "image/png" : "image/jpeg", .9);
          outs.push({ name: `${base(f.name)}-${String(i).padStart(digits, "0")}.${this.png ? "png" : "jpg"}`, blob, compare: false });
          ctx.state(f, "working", i / doc.numPages);
        }
        return outs;
      });
    },
  },

  rotate: {
    accept: "pdf", noDisplay: true,
    init() { this.pages = []; this.loaded = null; },
    build(core, ctx) {
      this.grid = h("div", { class: "pages" });
      const btn = (t, fn) => h("button", { class: "tbtn", type: "button", onclick: () => { fn(); this.paint(); } }, t);
      core.append(h("div", { class: "tools-row" },
        btn("Turn left", () => this.sel().forEach(p => p.rot -= 90)), btn("Turn right", () => this.sel().forEach(p => p.rot += 90)),
        btn("← Earlier", () => this.move(-1)), btn("Later →", () => this.move(1)),
        btn("Delete", () => { if (this.pages.some(p => !p.on)) this.pages = this.pages.filter(p => !p.on); }),
        btn("Select all", () => { const all = this.pages.every(p => p.on); this.pages.forEach(p => p.on = !all); })),
        h("p", { class: "hint" }, "Works on the first PDF on the left. Click pages to pick them, then turn, move or delete."), this.grid);
    },
    sel() { return this.pages.filter(p => p.on); },
    move(dir) {
      const idx = this.pages.map((p, i) => p.on ? i : -1).filter(i => i >= 0);
      if (dir > 0) idx.reverse();
      for (const i of idx) { const j = i + dir; if (j < 0 || j >= this.pages.length || this.pages[j].on) continue; [this.pages[i], this.pages[j]] = [this.pages[j], this.pages[i]]; }
    },
    paint() {
      this.grid.replaceChildren(...this.pages.map((p, i) => {
        const b = h("button", { class: "pg", type: "button", "aria-pressed": String(p.on), onclick: () => { p.on = !p.on; this.paint(); } }, p.canvas,
          h("span", { class: "n" }, (i + 1) + (((p.rot % 360) + 360) % 360 ? ` · turned ${((p.rot % 360) + 360) % 360}°` : "")));
        p.canvas.style.transform = `rotate(${p.rot}deg) scale(${((p.rot % 180) + 180) % 180 ? .72 : 1})`;
        return b;
      }));
    },
    async filesChanged(files) {
      const f = files[0];
      if (!f) { this.pages = []; this.loaded = null; this.paint(); return; }
      if (this.loaded === f) return;
      this.loaded = f;
      const lib = await pdfjs();
      const doc = await lib.getDocument({ data: new Uint8Array(await f.file.arrayBuffer()) }).promise;
      const pages = [];
      for (let i = 1; i <= doc.numPages; i++) {
        const page = await doc.getPage(i);
        const vp0 = page.getViewport({ scale: 1 });
        const vp = page.getViewport({ scale: 180 / Math.max(vp0.width, vp0.height) });
        const c = canvas(vp.width, vp.height);
        await page.render({ canvasContext: c.getContext("2d"), viewport: vp }).promise;
        pages.push({ src: i - 1, rot: 0, on: false, canvas: c });
        if (this.loaded !== f) return;
      }
      this.pages = pages;
      this.paint();
    },
    display() { return ["", "", "", ""]; },
    action() { return "Save the new PDF"; },
    validate() { return this.pages.length ? null : "Add a PDF and wait for its pages to show."; },
    async run(files, ctx) {
      const PDFLib = await pdfLib();
      const order = this.pages.map(p => [p.src, p.rot]);
      const f = files[0];
      await together([f], ctx, async () => {
        const src = await loadPdf(f, PDFLib);
        const out = await PDFLib.PDFDocument.create();
        const copied = await out.copyPages(src, order.map(o => o[0]));
        copied.forEach((p, i) => { p.setRotation(PDFLib.degrees((((p.getRotation().angle + order[i][1]) % 360) + 360) % 360)); out.addPage(p); });
        return [{ name: base(f.name) + "-edited.pdf", blob: pdfBlob(await out.save()) }];
      });
    },
  },
};

// ───────────── The machine ─────────────
const tool = TOOLS[mount.dataset.tool];
tool.init({ target: mount.dataset.target });
const files = [], outputs = [];
let busy = false, saved = 0;

const picker = h("input", { type: "file", multiple: true, hidden: true, accept: tool.accept === "pdf" ? ".pdf,application/pdf" : "image/*,.heic,.heif,.tif,.tiff" });
const inCards = h("div", { class: "cards" });
const drop = h("button", { class: "drop", type: "button", onclick: () => picker.click() }, tool.accept === "pdf" ? "+ Choose PDFs, or drop them here" : "+ Choose pictures, or drop them here");
const inNote = h("span", { class: "mono", style: "color:var(--muted)" });
const clearIn = h("button", { class: "clear", type: "button", onclick: () => { if (!busy) { files.length = 0; renderIn(); } } }, "Clear");
const side1 = h("div", { class: "side in" }, h("div", { class: "side-head" }, h("span", { class: "lbl" }, "In"), inNote, clearIn), inCards, drop, picker);

const display = tool.noDisplay ? null : h("div", { class: "display", "aria-live": "polite" });
const options = h("div", { style: "display:flex;flex-direction:column;gap:20px" });
const go = h("button", { class: "go", type: "button", onclick: run });
const status = h("p", { class: "status", role: "status" });
const core = h("div", { class: "core" }, display, options, go, status);

const outCards = h("div", { class: "cards" });
const outNote = h("span", { class: "mono", style: "color:var(--ok)" });
const zipBtn = h("button", { class: "zip", type: "button", hidden: true, onclick: zipAll }, "Download all (ZIP)");
const outEmpty = h("div", {}, h("div", { class: "ghost-slot" }), h("p", { class: "note" }, "Finished files show up here, ready to download."));
const side3 = h("div", { class: "side out" }, h("div", { class: "side-head" }, h("span", { class: "lbl" }, "Out"), outNote), outCards, outEmpty, zipBtn,
  h("p", { class: "note" }, "Done on your device. Nothing was uploaded."));
const machine = h("div", { class: "machine" }, side1, core, side3);
mount.replaceChildren(machine);

tool.build(options, { changed: refresh });

function refresh() {
  if (display) {
    const [from, to, fn, tn] = tool.display(files);
    display.replaceChildren(
      h("div", { class: "col" }, h("span", { class: "lbl" }, "From"), h("span", { class: "big" }, from), h("span", { class: "note" }, fn)),
      h("div", { class: "trail", "aria-hidden": "true" }, h("i"), h("i"), h("i"),
        (() => { const s = document.createElementNS("http://www.w3.org/2000/svg", "svg"); s.setAttribute("viewBox", "0 0 30 24"); s.innerHTML = '<path d="M0 12h24M16 4l8 8-8 8" fill="none" stroke="currentColor" stroke-width="4" style="color:var(--red)"/>'; return s; })()),
      h("div", { class: "col to" }, h("span", { class: "lbl" }, "To"), h("span", { class: "big" }, to), h("span", { class: "note" }, tn)));
  }
  go.textContent = busy ? "Working…" : tool.action(files);
  go.disabled = busy || !files.length;
}

function renderIn() {
  machine.classList.toggle("has-files", files.length > 0);
  inNote.textContent = files.length ? `${files.length} · ${size(files.reduce((a, f) => a + f.size, 0))}` : "";
  drop.textContent = files.length ? "+ Add more" : (tool.accept === "pdf" ? "+ Choose PDFs, or drop them here" : "+ Choose pictures, or drop them here");
  inCards.replaceChildren(...files.map((f, i) => {
    const sub = f.state === "failed" ? f.error : f.state === "working" ? "" : (f.state === "done" ? "Done · " : f.state === "waiting" ? "Waiting · " : "") + f.note;
    const card = h("div", { class: "card " + (f.state || ""), draggable: tool.reorder && !busy ? "true" : null },
      f.thumb ? h("img", { class: "thumb", src: f.thumb, alt: "" }) : h("div", { class: "thumb" }),
      h("div", { class: "txt" }, h("span", { class: "name", title: f.name }, f.name), sub ? h("span", { class: "sub" }, sub) : null,
        f.state === "working" ? h("div", { class: "bar" }, h("i", { style: `width:${Math.round((f.p || .3) * 100)}%` })) : null),
      h("span", { class: "badge" }, fmtName(f.name)),
      busy ? null : h("button", { class: "x", type: "button", "aria-label": "Remove " + f.name, onclick: () => { files.splice(i, 1); changedFiles(); } }, "✕"));
    if (tool.reorder) {
      card.addEventListener("dragstart", e => { e.dataTransfer.setData("text/vk", String(i)); e.dataTransfer.effectAllowed = "move"; });
      card.addEventListener("dragover", e => { if (e.dataTransfer.types.includes("text/vk")) { e.preventDefault(); card.classList.add("drag-over"); } });
      card.addEventListener("dragleave", () => card.classList.remove("drag-over"));
      card.addEventListener("drop", e => {
        const from = +e.dataTransfer.getData("text/vk"); if (isNaN(from)) return;
        e.preventDefault(); e.stopPropagation();
        const [m] = files.splice(from, 1); files.splice(i, 0, m); changedFiles();
      });
    }
    return card;
  }));
  refresh();
}
function changedFiles() { renderIn(); if (tool.filesChanged) Promise.resolve(tool.filesChanged(files)).catch(e => showStatus(e.message, true)); }

function add(list) {
  const want = tool.accept === "pdf" ? isPdf : isImage;
  const ok = [...list].filter(want), skipped = list.length - ok.length;
  for (const file of ok) {
    if (files.some(f => f.name === file.name && f.size === file.size)) continue;
    const f = { file, name: file.name, size: file.size, note: size(file.size) };
    files.push(f);
    details(f);
  }
  if (skipped) {
    const other = tool.accept === "pdf" ? "pictures (try JPG to PDF)" : "PDFs (try the PDF tools)";
    showStatus(`${plural(skipped, "file")} skipped: this tool takes ${tool.accept === "pdf" ? "PDFs" : "pictures"}, not ${other}.`, true);
  }
  changedFiles();
}

async function details(f) {
  try {
    if (tool.accept === "pdf") {
      const lib = await pdfjs();
      const doc = await lib.getDocument({ data: new Uint8Array(await f.file.arrayBuffer()) }).promise;
      f.pages = doc.numPages;
      const page = await doc.getPage(1);
      const vp0 = page.getViewport({ scale: 1 }), vp = page.getViewport({ scale: 96 / Math.max(vp0.width, vp0.height) });
      const c = canvas(vp.width, vp.height), x = c.getContext("2d");
      x.fillStyle = "#fff"; x.fillRect(0, 0, c.width, c.height);
      await page.render({ canvasContext: x, viewport: vp }).promise;
      f.thumb = c.toDataURL("image/png");
      f.note = `${plural(f.pages, "page")} · ${size(f.size)}`;
    } else {
      const src = await decode(f.file);
      f.w = W(src); f.h = H(src);
      const s = 96 / Math.max(f.w, f.h);
      f.thumb = draw(src, Math.max(1, f.w * s), Math.max(1, f.h * s)).toDataURL("image/png");
      f.note = `${f.w} × ${f.h} · ${size(f.size)}`;
    }
  } catch (e) {
    f.note = /password|locked/i.test(e.message || "") ? "Locked with a password" : (tool.accept === "pdf" ? "Can’t read this PDF" : "Can’t open this picture here");
  }
  if (!busy) renderIn();
}

picker.addEventListener("change", () => { add(picker.files); picker.value = ""; });
let dragDepth = 0;
const dragHasFiles = e => [...(e.dataTransfer && e.dataTransfer.types || [])].includes("Files");
window.addEventListener("dragenter", e => { if (dragHasFiles(e)) { dragDepth++; machine.classList.add("dragging"); } });
window.addEventListener("dragleave", e => { if (dragHasFiles(e) && --dragDepth <= 0) { dragDepth = 0; machine.classList.remove("dragging"); } });
window.addEventListener("dragover", e => { if (dragHasFiles(e)) e.preventDefault(); });
window.addEventListener("drop", e => {
  if (!dragHasFiles(e)) return;
  e.preventDefault(); dragDepth = 0; machine.classList.remove("dragging");
  if (!busy) add(e.dataTransfer.files);
});
window.addEventListener("paste", e => { const fl = e.clipboardData && e.clipboardData.files; if (fl && fl.length && !busy) add(fl); });

function showStatus(text, err) { status.textContent = text; status.classList.toggle("err", !!err); }

async function run() {
  if (busy || !files.length) return;
  const problem = tool.validate && tool.validate(files);
  if (problem) return showStatus(problem, true);
  busy = true;
  files.forEach(f => { f.state = "waiting"; f.error = null; });
  showStatus("Working… everything stays on this device.");
  renderIn();
  const started = performance.now();
  const before = outputs.length;
  await tool.run([...files], {
    state(f, s, p, err) { f.state = s; f.p = p; f.error = err; renderIn(); },
    output(name, blob, src) { addOutput(name, blob, src); },
  });
  busy = false;
  renderIn();
  const failed = files.filter(f => f.state === "failed").length, made = outputs.length - before;
  const secs = ((performance.now() - started) / 1000).toFixed(1);
  if (failed) showStatus(`${failed} of ${files.length} couldn’t be done. The red card says why.`, true);
  else showStatus(`Done in ${secs} s · ${plural(made, "file")} ready on the right.`);
  if (made && window.matchMedia("(max-width: 1000px)").matches) side3.scrollIntoView({ behavior: "smooth", block: "start" });
}

function addOutput(name, blob, src) {
  // Two inputs can make the same name (photo.heic and photo.jpg → photo.jpg): number the later one.
  for (let k = 1; outputs.some(o => o.name === name); k++) name = `${base(name).replace(/ \(\d+\)$/, "")} (${k}).${ext(name)}`;
  const o = { name, blob, url: URL.createObjectURL(blob), src };
  outputs.unshift(o);
  if (src && blob.size < src.size) saved += src.size - blob.size;
  renderOut();
}

function renderOut() {
  outEmpty.hidden = outputs.length > 0;
  zipBtn.hidden = outputs.length < 2;
  outNote.textContent = saved ? "saved " + size(saved) : outputs.length ? plural(outputs.length, "file") : "";
  outCards.replaceChildren(...outputs.map(o => {
    const good = o.src && o.blob.size <= o.src.size;
    const isImg = o.blob.type.startsWith("image/") && o.blob.type !== "image/x-icon" && o.blob.type !== "image/bmp";
    return h("div", { class: "card" },
      isImg ? h("img", { class: "thumb", src: o.url, alt: "" }) : h("div", { class: "thumb", style: "display:grid;place-items:center;font:700 10px var(--mono);color:var(--muted)" }, fmtName(o.name)),
      h("div", { class: "txt" }, h("span", { class: "name", title: o.name }, o.name),
        h("span", { class: "sub" }, size(o.blob.size), o.src ? h("span", { style: `color:var(${good ? "--ok" : "--muted"});font-weight:700` }, " · " + change(o.src.size, o.blob.size)) : null)),
      h("a", { class: "dl", href: o.url, download: o.name }, "Save"));
  }));
}

async function zipAll() {
  zipBtn.disabled = true; zipBtn.textContent = "Making ZIP…";
  try {
    await lib("jszip.min.js");
    const zip = new window.JSZip(), used = new Set();
    for (const o of outputs) {
      let n = o.name, k = 1;
      while (used.has(n)) n = `${base(o.name)} (${k++}).${ext(o.name)}`;
      used.add(n); zip.file(n, o.blob);
    }
    const blob = await zip.generateAsync({ type: "blob" });
    const a = h("a", { href: URL.createObjectURL(blob), download: "VKonvert files.zip" });
    document.body.append(a); a.click(); a.remove();
  } catch (e) { showStatus(e.message, true); }
  zipBtn.disabled = false; zipBtn.textContent = "Download all (ZIP)";
}

renderIn();
renderOut();
})();
