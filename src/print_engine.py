"""
Print engine - handles actual printing on Windows via Win32 API.
Falls back to CLI (lp/lpr) on Unix for development/testing.
"""
import subprocess
import sys
import os
import tempfile
import shutil
from pathlib import Path
from typing import Optional


class PrintEngine:
    """Cross-platform print execution."""

    def __init__(self, printer_name: Optional[str] = None):
        self.printer_name = printer_name or self._default_printer()

    def _default_printer(self) -> str:
        """Detect default printer."""
        if sys.platform == "win32":
            return self._windows_default_printer()
        # Unix: try LPstat
        try:
            result = subprocess.run(
                ["lpstat", "-d"], capture_output=True, text=True, timeout=5
            )
            if result.returncode == 0 and "no default device" not in result.stdout:
                # parse default printer
                for line in result.stdout.splitlines():
                    if "system default device:" in line:
                        return line.split(":")[-1].strip()
        except Exception:
            pass
        return "DEFAULT_PRINTER"

    def _windows_default_printer(self) -> str:
        try:
            import win32print  # type: ignore

            return win32print.GetDefaultPrinter()
        except ImportError:
            return "Microsoft Print to PDF"

    def print_file(self, path: Path, file_type: str) -> tuple[bool, str]:
        """
        Print a file. Returns (success, message).
        """
        if not path.exists():
            return False, f"File not found: {path}"

        if sys.platform == "win32":
            return self._windows_print(path, file_type)
        else:
            return self._unix_print(path)

    def _unix_print(self, path: Path) -> tuple[bool, str]:
        """Use lp command on Unix."""
        try:
            result = subprocess.run(
                ["lp", "-d", self.printer_name, str(path)],
                capture_output=True,
                text=True,
                timeout=60,
            )
            if result.returncode == 0:
                return True, f"Printed via lp: {path.name}"
            else:
                return False, f"lp failed: {result.stderr}"
        except FileNotFoundError:
            return False, "lp command not found (not a print server)"
        except subprocess.TimeoutExpired:
            return False, "Print timed out"
        except Exception as e:
            return False, str(e)

    def _windows_print(self, path: Path, file_type: str) -> tuple[bool, str]:
        """Use Windows API or PDF virtual printer."""
        # Try using default PDF print approach via shell
        try:
            # Use Windows shell to print
            import win32api  # type: ignore
            import win32print  # type: ignore
            import win32com.client  # type: ignore

            # Method 1: ShellExecute print
            import pythoncom  # type: ignore

            pythoncom.CoInitialize()
            try:
                # Open the file with the default printer
                result = win32api.ShellExecute(
                    0, "print", str(path), None, None, 0
                )
                if result > 32:
                    return True, f"Printed via ShellExecute: {path.name}"
                else:
                    return False, f"ShellExecute failed with code {result}"
            finally:
                pythoncom.CoUninitialize()
        except ImportError as e:
            return False, f"Windows print libraries not available: {e}"
        except Exception as e:
            return False, f"Windows print error: {e}"

    def list_printers(self) -> list[str]:
        """List available printers."""
        if sys.platform == "win32":
            try:
                import win32print  # type: ignore

                printers = []
                for info in win32print.EnumPrinters(
                    win32print.PRINTER_ENUM_LOCAL
                    | win32print.PRINTER_ENUM_CONNECTIONS
                ):
                    printers.append(info[2])
                return printers
            except ImportError:
                return ["Microsoft Print to PDF"]
        else:
            try:
                result = subprocess.run(
                    ["lpstat", "-a"], capture_output=True, text=True, timeout=5
                )
                if result.returncode == 0:
                    return [
                        line.split()[0]
                        for line in result.stdout.splitlines()
                        if line.strip()
                    ]
            except Exception:
                pass
            return []
