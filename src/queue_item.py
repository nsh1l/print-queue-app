"""
Print queue item model.
"""
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Optional


class FileStatus(Enum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    PRINTING = "PRINTING"
    DONE = "DONE"
    ERROR = "ERROR"


@dataclass
class QueueItem:
    id: str
    name: str
    path: Path
    size: int
    added_at: datetime
    status: FileStatus = FileStatus.PENDING
    error: Optional[str] = None
    print_result: Optional[str] = None

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "path": str(self.path),
            "size": self.size,
            "added_at": self.added_at.isoformat(),
            "status": self.status.value,
            "error": self.error,
            "print_result": self.print_result,
        }

    @property
    def size_str(self) -> str:
        size = self.size
        for unit in ["B", "KB", "MB", "GB"]:
            if size < 1024:
                return f"{size:.1f} {unit}"
            size /= 1024
        return f"{size:.1f} TB"
