require "google/cloud/firestore"


firestore = Google::Cloud::Firestore.new(
  project_id: 'ori-tracker',
  credentials: 'firebase-adminsdk-credentials.json'
)

# Create a query
query = firestore.doc("randoAreas/primary")

results = query.get

puts JSON.pretty_generate(results.data)
