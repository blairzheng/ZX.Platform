import getpass
import json
import re
import subprocess
from pathlib import Path


def prompt(label: str) -> str:
    value = getpass.getpass(label)
    if not value.strip():
        raise SystemExit(f"{label.rstrip(': ')} cannot be empty")
    return value


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    project = root / "Services/Identity/ZX.Platform.Identity.Api"
    if not (project / "ZX.Platform.Identity.Api.csproj").is_file():
        raise SystemExit("Identity API project was not found")

    schema = prompt("Test database schema: ").strip().upper()
    if not re.fullmatch(r"[A-Z][A-Z0-9_]{0,127}", schema):
        raise SystemExit("Schema must be an Oracle identifier in uppercase")

    developer = prompt("Developer ID (letters, digits, _ or -): ").strip()
    if not re.fullmatch(r"[A-Za-z0-9_-]+", developer):
        raise SystemExit("Developer ID may contain only letters, digits, _ or -")

    settings = {
        "ConnectionStrings": {
            "IdentityDatabase": prompt("Test database connection string: "),
            "IdentityRedis": prompt("Development Redis/Sentinel connection string: "),
        },
        "IdentityDatabase": {"Schema": schema},
        "AuthenticationRedis": {
            "KeyPrefix": f"mom:development:{developer}:identity:auth:"
        },
    }

    subprocess.run(
        ["dotnet", "user-secrets", "set", "--project", str(project)],
        input=json.dumps(settings),
        text=True,
        check=True,
    )
    print(f"Development settings saved locally with Redis prefix for {developer}.")


if __name__ == "__main__":
    main()
