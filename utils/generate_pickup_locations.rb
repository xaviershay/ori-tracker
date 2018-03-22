require 'open-uri'
require 'nokogiri'

url = "https://raw.githubusercontent.com/sigmasin/OriDERandomizer/HEAD/seed_gen/areas.xml"

raw_body = open(url)

doc = Nokogiri::XML(raw_body)

locations = doc.xpath("Areas/Area/Locations/Location")

locations.each do |location|
  row = [
    location.at_xpath("Item").text,
    location.at_xpath("X").text,
    location.at_xpath("Y").text,
  ]

  puts row.join(",")
end
