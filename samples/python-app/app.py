"""Acme Notes (Python edition): a Tkinter app that works only with a license or an active trial.

Run:  python app.py
"""
import threading
import tkinter as tk
import webbrowser
from pathlib import Path
from tkinter import messagebox, simpledialog

from licensing import LicenseManager, LicenseState, load_settings, user_data_dir

APP_VERSION = "1.0.0"
HERE = Path(__file__).parent


class App(tk.Tk):
    def __init__(self, license: LicenseManager):
        super().__init__()
        self.license = license
        self.title(f"Acme Notes {APP_VERSION}")
        self.geometry("760x460")

        bar = tk.Frame(self, padx=8, pady=8)
        bar.pack(fill="x")
        self.status = tk.Label(bar, text="Checking license…")
        self.status.pack(side="left")
        self.buttons = {
            "activate": tk.Button(bar, text="Enter license key…", command=self.on_activate),
            "buy": tk.Button(bar, text="Buy", command=lambda: webbrowser.open(license.settings.buy_url)),
            "deactivate": tk.Button(bar, text="Deactivate this computer", command=self.on_deactivate),
            "retry": tk.Button(bar, text="Retry", command=self.refresh),
        }
        self.update_label = tk.Label(bar, fg="royal blue")
        self.editor = tk.Text(self, wrap="word", font=("Segoe UI", 11))
        self.editor.insert("1.0", "Welcome to Acme Notes.\n\nThis editor only works while you have a license or an active trial.")
        self.editor.pack(fill="both", expand=True, padx=8, pady=(0, 8))
        self.refresh()  # check on every start

    # Network calls run on a worker thread so the window never freezes; results come back via after().
    def run(self, work, done):
        threading.Thread(target=lambda: self.after(0, done, work()), daemon=True).start()

    def refresh(self):
        self.status.config(text="Checking license…", fg="black")
        self.run(self.license.check, self.show)

    def show(self, s: LicenseState):
        self.status.config(text=s.message, fg=("dark orange" if s.offline else "dark green") if s.can_use else "firebrick")
        self.editor.config(state="normal" if s.can_use else "disabled")
        visible = {
            "activate": s.kind not in ("licensed", "update-required"),
            "buy": s.kind in ("trial", "trial-ended", "expired", "not-licensed"),
            "deactivate": s.kind == "licensed" and not s.offline,
            "retry": s.kind == "needs-connection" or s.offline,
        }
        self.buttons["buy"].config(text="Renew" if s.kind == "expired" else "Buy")
        for name, button in self.buttons.items():
            button.pack_forget()
            if visible[name]:
                button.pack(side="left", padx=(8, 0))
        self.update_label.pack_forget()
        if s.update_available:
            self.update_label.config(text="Please install the latest version." if s.kind == "update-required"
                                     else f"Version {s.update_available} is available.")
            self.update_label.pack(side="left", padx=(8, 0))

    def on_activate(self):
        key = simpledialog.askstring("Activate", "License key:", initialvalue=self.license.stored_key or "", parent=self)
        if not key:
            return
        email = simpledialog.askstring("Activate", "Email used to buy:", parent=self)
        if not email:
            return
        self.status.config(text="Activating…", fg="black")

        def done(result):
            ok, message = result
            (messagebox.showinfo if ok else messagebox.showwarning)("Activation", message, parent=self)
            self.refresh()
        self.run(lambda: self.license.activate(key, email), done)

    def on_deactivate(self):
        if not messagebox.askokcancel("Deactivate", "Deactivate Acme Notes on this computer? "
                                      "You can then activate the key on another one.", parent=self):
            return

        def done(result):
            ok, message = result
            (messagebox.showinfo if ok else messagebox.showwarning)("Deactivate", message, parent=self)
            self.refresh()
        self.run(self.license.deactivate, done)


def main():
    try:
        settings = load_settings(HERE / ".env")
    except ValueError as e:
        root = tk.Tk()
        root.withdraw()
        messagebox.showwarning("Acme Notes: configuration", str(e))
        return
    license = LicenseManager(settings, APP_VERSION, user_data_dir("AcmeNotesPy") / "license-cache.json")
    App(license).mainloop()


if __name__ == "__main__":
    main()
