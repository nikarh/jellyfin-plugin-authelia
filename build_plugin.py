import os
import sys
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--version', required=True)
parser.add_argument('--prerelease')
opts = parser.parse_args()

version = opts.version
prerelease = bool(opts.prerelease)

artifact_dir = os.path.join(os.getcwd(), 'artifacts')
os.mkdir(artifact_dir)


jellyfin_repo_file="./manifest.json"
jellyfin_repo_url="https://github.com/nikarh/jellyfin-plugin-authelia/releases/download"

zipfile=os.popen('jprm --verbosity=debug plugin build "." --output="%s" --version="%s" --dotnet-framework="net9.0"' % (artifact_dir, version)).read().strip()

os.system('jprm repo add --url=%s %s %s' % (jellyfin_repo_url, jellyfin_repo_file, zipfile))
os.system('sed -i "s/authelia-authentication\//%s\//" %s' % (version, jellyfin_repo_file))

print(version)
