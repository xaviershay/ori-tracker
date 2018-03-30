require "google/cloud/firestore"

firestore = Google::Cloud::Firestore.new(
  project_id: 'ori-tracker',
  credentials: 'firebase-adminsdk-credentials.json'
)

data = JSON.load(File.read(ARGV.fetch(0)))

firestore.batch do |b|
  data.each do |area, points|
    b.set("randoAreas/#{area}", points: points)
  end
end
