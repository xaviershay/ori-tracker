require "google/cloud/firestore"

firestore = Google::Cloud::Firestore.new(
  project_id: 'ori-tracker',
  credentials: 'firebase-adminsdk-credentials.json'
)

# Create a query
data = firestore.collection("randoAreas").get

json = data.map do |doc|
  points = doc.data.fetch(:points, nil)
  next unless points
  [doc.ref.document_id, points]
end.compact.to_h

puts JSON.pretty_generate(json)
