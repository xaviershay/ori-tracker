require 'open-uri'
require 'nokogiri'

url = "https://raw.githubusercontent.com/sigmasin/OriDERandomizer/HEAD/seed_gen/areas.xml"

raw_body = open(url)

doc = Nokogiri::XML(raw_body)

locations = doc.xpath("Areas/Area")

locations.each do |location|
  row = [
    location['name']
  ]

  puts row.join(",")
end
