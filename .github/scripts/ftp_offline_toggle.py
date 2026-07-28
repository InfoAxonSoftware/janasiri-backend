#!/usr/bin/env python3
"""
Uploads or removes app_offline.htm on the MonsterASP staging FTP site, using only the
standard-library ftplib. Connection details come exclusively from environment variables —
never hardcode or print the password.

Usage:
    python3 ftp_offline_toggle.py upload <local_file_path>
    python3 ftp_offline_toggle.py remove

Required environment variables:
    FTP_SERVER       - FTP host (plain FTP, port 21)
    FTP_USERNAME     - FTP username
    FTP_PASSWORD     - FTP password (never printed or logged)
    FTP_SERVER_DIR   - remote site root directory (normalized before use, see below)
"""

import ftplib
import os
import posixpath
import sys

REMOTE_FILENAME = "app_offline.htm"


def normalize_remote_dir(raw: str) -> str:
    """
    Force an absolute, traversal-free FTP directory path. The configured server directory is
    never trusted as-is: it's made absolute, normalized, and explicitly checked for any
    remaining '..' segment before ftp.cwd() is ever called with it.
    """
    raw = (raw or "").strip()
    if not raw:
        return "/"
    if not raw.startswith("/"):
        raw = "/" + raw
    normalized = posixpath.normpath(raw)
    if normalized in ("", "."):
        normalized = "/"
    parts = [p for p in normalized.split("/") if p]
    if any(part == ".." for part in parts):
        raise ValueError(f"Refusing unsafe remote directory containing '..': {raw!r}")
    if not normalized.startswith("/"):
        normalized = "/" + normalized
    return normalized


def connect() -> ftplib.FTP:
    server = os.environ["FTP_SERVER"]
    username = os.environ["FTP_USERNAME"]
    password = os.environ["FTP_PASSWORD"]
    remote_dir = normalize_remote_dir(os.environ.get("FTP_SERVER_DIR", "/"))

    print(f"Connecting to {server}:21 (plain FTP)...")
    ftp = ftplib.FTP()
    ftp.connect(server, 21, timeout=30)
    ftp.login(username, password)
    ftp.cwd(remote_dir)
    print(f"Connected. Working directory: {remote_dir}")
    return ftp


def upload(local_file: str) -> None:
    ftp = connect()
    try:
        with open(local_file, "rb") as f:
            ftp.storbinary(f"STOR {REMOTE_FILENAME}", f)
        print(f"{REMOTE_FILENAME} uploaded.")
    finally:
        _quiet_quit(ftp)


def remove() -> None:
    ftp = connect()
    try:
        ftp.delete(REMOTE_FILENAME)
        print(f"{REMOTE_FILENAME} removed.")
    finally:
        _quiet_quit(ftp)


def _quiet_quit(ftp: ftplib.FTP) -> None:
    try:
        ftp.quit()
    except Exception:
        ftp.close()


def main() -> None:
    if len(sys.argv) < 2 or sys.argv[1] not in ("upload", "remove"):
        print("Usage: ftp_offline_toggle.py <upload <local_file>|remove>", file=sys.stderr)
        sys.exit(2)

    action = sys.argv[1]
    if action == "upload":
        if len(sys.argv) != 3:
            print("Usage: ftp_offline_toggle.py upload <local_file>", file=sys.stderr)
            sys.exit(2)
        upload(sys.argv[2])
    else:
        remove()


if __name__ == "__main__":
    main()
