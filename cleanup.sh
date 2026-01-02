#!/bin/bash
echo "🧹 Cleaning up project..."

# Remove old test files
echo "Removing test files..."
rm -f PhotogrammetryAPI/*.obj
rm -f PhotogrammetryAPI/*.zip
rm -f PhotogrammetryAPI/*.ply
rm -f PhotogrammetryAPI/doc_review.txt
rm -f PhotogrammetryAPI/README_update.sh
rm -f PhotogrammetryAPI/README_COLMAP_UPDATES.txt

# Remove test data directories
echo "Removing test data..."
rm -rf PhotogrammetryAPI/uploads/
rm -rf PhotogrammetryAPI/models/
rm -rf images/

# Remove outdated documentation
echo "Removing outdated documentation..."
rm -f INSTALLATION.md
rm -f SUMMARY.md
rm -f TEST_RESULTS.md
rm -f COLMAP_MIGRATION.md
rm -f COLMAP_INTEGRATION_SUMMARY.md
rm -f PhotogrammetryAPI/README_COLMAP.md
rm -f PhotogrammetryAPI/FINAL_STATUS.md
rm -f PhotogrammetryAPI/HOW_TO_TEST.md
rm -f PhotogrammetryAPI/RUNNING.md

# Rename updates to changelog
echo "Creating CHANGELOG.md..."
if [ -f PhotogrammetryAPI/UPDATES_2026-01-02.md ]; then
    mv PhotogrammetryAPI/UPDATES_2026-01-02.md PhotogrammetryAPI/CHANGELOG.md
fi

# Remove doc review file
rm -f doc_review.txt

echo "✅ Cleanup complete!"
echo ""
echo "Remaining documentation:"
find . -name "*.md" -type f ! -path "*/bin/*" ! -path "*/obj/*" | sort
